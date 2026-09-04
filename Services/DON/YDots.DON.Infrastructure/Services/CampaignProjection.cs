using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;
using YDots.DON.Infrastructure.Persistence;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Keeps DON's local copy of the campaign list in step with the one CAM owns.
///
/// WHY THIS EXISTS. DON has always had its own <c>don_campaigns</c> table, and
/// <c>don_leads.campaign_id</c> carries a foreign key to it, so a lead can only ever name a row
/// that lives here. Nothing populated it. The three rows the seeder wrote belonged to a hard-coded
/// organisation id that matches no real Organisation, so every Lead Capture screen in the platform
/// offered an EMPTY campaign list, and the campaign somebody had just created in CAM was nowhere to
/// be found. The two modules each had a campaigns table and no connection between them.
///
/// WHAT THIS DOES ABOUT IT. CAM remains the owner: campaigns are created, approved and closed
/// there and nowhere else. This reads that table and mirrors what it finds into DON's, KEYED BY
/// THE SAME ID, so a campaign means the same thing on both sides of the seam and the foreign key
/// is satisfied without inventing a second identity for the same campaign.
///
/// THE WRITE IS THE UNCOMFORTABLE PART, and worth being plain about: a read of the Lead Capture
/// screen can insert rows. It is confined to a local read-model that DON alone uses, it copies
/// rather than decides, and every value comes from CAM - but it is a write on a GET, and if
/// campaigns ever move to a database of their own this is the class that has to become a real
/// subscription rather than a query.
///
/// NOTHING HERE THROWS. If the campaign tables cannot be reached, the screen shows whatever DON
/// already had rather than failing to open. A short campaign list is visibly short; a page that
/// will not load tells nobody anything.
/// </summary>
public sealed class CampaignProjection(
    DonDbContext context,
    ILogger<CampaignProjection> logger)
{
    /// <summary>
    /// CAM's statuses that a lead may be captured against.
    ///
    /// SCHEDULED WAS MISSING, and it is the commonest of the four. Approving a campaign whose
    /// lifecycle activation is automatic does not leave it Approved - it leaves it SCHEDULED,
    /// waiting for its own start date, which is what the readiness screen's Approve launch
    /// produces for every campaign set up that way. Those campaigns were never mirrored into DON,
    /// so they were absent from the campaign picker on Lead Capture, the Lead Work Queue and the
    /// Assignment Board: an Organisation whose campaigns were all approved-and-scheduled saw an
    /// empty dropdown and could not capture a lead at all.
    ///
    /// DRAFT AND SUBMITTED ARE STILL EXCLUDED deliberately: a campaign nobody has approved yet is
    /// not something to be taking donor interest against. Closing, Closed and Cancelled are
    /// excluded for the same reason DON's own query excludes Closed.
    ///
    /// KEEP THIS IN STEP WITH <see cref="Translate"/>, which decides what each of these becomes
    /// on DON's side.
    /// </summary>
    private const string OfferableStatuses = "('Active', 'Approved', 'Scheduled', 'Paused')";

    public async Task RefreshAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        if (organisationId == Guid.Empty)
        {
            return;
        }

        try
        {
            var live = await ReadFromCampaignsAsync(organisationId, cancellationToken);

            if (live.Count == 0)
            {
                return;
            }

            var mine = await context.Campaigns
                .Where(campaign => campaign.OrganisationId == organisationId)
                .ToDictionaryAsync(campaign => campaign.Id, cancellationToken);

            var changed = 0;

            foreach (var row in live)
            {
                if (mine.TryGetValue(row.Id, out var existing))
                {
                    // Only what CAM owns is copied over. Anything DON has added locally about a
                    // campaign is left alone.
                    if (existing.Code == row.Code
                        && existing.Name == row.Name
                        && existing.Status == row.Status
                        && existing.StartsAtUtc == row.StartsAtUtc
                        && existing.EndsAtUtc == row.EndsAtUtc)
                    {
                        continue;
                    }

                    existing.Code = row.Code;
                    existing.Name = row.Name;
                    existing.Status = row.Status;
                    existing.StartsAtUtc = row.StartsAtUtc;
                    existing.EndsAtUtc = row.EndsAtUtc;
                    changed++;

                    continue;
                }

                context.Campaigns.Add(new Campaign
                {
                    Id = row.Id,
                    OrganisationId = organisationId,
                    Code = row.Code,
                    Name = row.Name,
                    Description = row.Description,
                    Status = row.Status,
                    StartsAtUtc = row.StartsAtUtc,
                    EndsAtUtc = row.EndsAtUtc,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty
                });

                changed++;
            }

            if (changed > 0)
            {
                await context.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Campaign list refreshed from CAM for organisation {OrganisationId}: "
                    + "{Changed} campaign(s) added or updated.",
                    organisationId, changed);
            }
        }
        catch (Exception exception) when (exception is PostgresException
                                              or NpgsqlException
                                              or DbUpdateException
                                              or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Could not refresh the campaign list from CAM for organisation {OrganisationId}. "
                + "The screen will show the campaigns already held.",
                organisationId);
        }
    }

    /// <summary>
    /// CAM's campaigns for one Organisation, read directly.
    ///
    /// The column list is deliberately narrow - id, code, name, status and dates. DON has no
    /// business reading a campaign's budget, its owners or its approval history, and asking for
    /// only what is mirrored keeps the seam honest and the query cheap.
    /// </summary>
    private async Task<List<CampaignRow>> ReadFromCampaignsAsync(
        Guid organisationId, CancellationToken cancellationToken)
    {
        const string Sql = """
            SELECT id, code, name, public_description, status, start_date, end_date
            FROM cam_campaigns
            WHERE tenant_id = @organisationId
              AND status IN
            """;

        var rows = new List<CampaignRow>();

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        if (opened)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql + OfferableStatuses;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "organisationId";
            parameter.Value = organisationId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CampaignRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Translate(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : ToUtc(reader.GetFieldValue<DateOnly>(5)),
                    reader.IsDBNull(6) ? null : ToUtc(reader.GetFieldValue<DateOnly>(6))));
            }
        }
        finally
        {
            if (opened)
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        return rows;
    }

    private static DateTimeOffset ToUtc(DateOnly value) =>
        new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// CAM's status vocabulary is richer than DON's. Approved, Scheduled and Paused all mean "a
    /// lead may be taken against it" as far as this module is concerned, so they arrive as Active.
    ///
    /// Only the rows selected by <see cref="OfferableStatuses"/> reach this, so the Closed branch
    /// is defensive: it keeps a campaign that has been closed since it was last mirrored from
    /// being resurrected as Active by a later refresh.
    /// </summary>
    private static CampaignStatus Translate(string status) =>
        status.Equals("Closed", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Closing", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            ? CampaignStatus.Closed
            : CampaignStatus.Active;

    private sealed record CampaignRow(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        CampaignStatus Status,
        DateTimeOffset? StartsAtUtc,
        DateTimeOffset? EndsAtUtc);
}
