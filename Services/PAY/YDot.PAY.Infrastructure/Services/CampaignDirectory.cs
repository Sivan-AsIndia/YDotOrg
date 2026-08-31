using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Campaign names and tracking-asset resolution, read from CAM's tables over the shared database.
///
/// EVERYTHING HERE IS READ-ONLY, which is the difference between this seam and the donor one.
/// PAY never writes a campaign; it only needs to render a name beside a donation and to turn a
/// QR code's reference into an attribution. A read-only seam over a shared database is a much
/// smaller commitment than a write one, and if campaigns ever move to their own database only
/// this class changes.
///
/// THE NAME LOOKUP TAKES A SET, NOT AN ID. A donation register page shows twenty rows
/// referencing perhaps four campaigns; asking per row would be twenty queries to render one
/// screen, and the N+1 would appear on the busiest page in the module.
/// </summary>
public sealed class CampaignDirectory(PaymentDbContext context, ILogger<CampaignDirectory> logger)
    : ICampaignDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetCampaignNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaignIds);

        var names = new Dictionary<Guid, string>();

        if (tenantId == Guid.Empty || campaignIds.Count == 0)
        {
            return names;
        }

        const string Sql = """
            SELECT id, name
            FROM cam_campaigns
            WHERE tenant_id = @tenant_id AND id = ANY(@ids)
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = campaignIds.Distinct().ToArray()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                names[reader.GetGuid(0)] = reader.GetString(1);
            }
        }
        catch (NpgsqlException exception)
        {
            // NOT RETHROWN. A missing campaign name degrades a grid to showing an identifier; it
            // is not a reason to fail the page. The donations themselves are what the operator
            // came for.
            logger.LogError(
                exception,
                "Could not resolve campaign names for organisation {TenantId}. "
                + "The affected rows will display without a campaign name.",
                tenantId);
        }

        return names;
    }

    public async Task<string?> GetCampaignNameAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken)
    {
        var names = await GetCampaignNamesAsync(tenantId, [campaignId], cancellationToken);

        return names.TryGetValue(campaignId, out var name) ? name : null;
    }

    /// <summary>
    /// Turns the reference from a QR code or link into its attribution.
    ///
    /// SECTION 22 DEPENDS ENTIRELY ON THIS. The reference is the only thing the donor's request
    /// carries, and it is what turns an anonymous gift into one attributed to a campaign, a
    /// channel, a source and a medium.
    ///
    /// IT IS ALSO HOW THE PUBLIC PATH RESOLVES AN ORGANISATION, which is why it takes no tenant
    /// id and reads across the whole table. The reference is unguessable and unique platform-wide,
    /// so it NAMES a record rather than choosing one - the same reasoning that makes the intent
    /// reference safe.
    ///
    /// THE JOINS ARE LEFT, not inner: a tracking asset whose channel row was later removed should
    /// still take a donation, attributed to its campaign with a blank channel. An inner join
    /// would make the whole link stop working and the donor would see nothing but an error.
    /// </summary>
    public async Task<TrackingAttribution?> ResolveTrackingReferenceAsync(
        string trackingReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackingReference))
        {
            return null;
        }

        const string Sql = """
            SELECT
                asset.id,
                asset.tenant_id,
                asset.business_unit_id,
                asset.campaign_id,
                campaign.name,
                channel.name,
                source.name,
                medium.name,
                asset.created_by_user_id,
                asset.status,
                asset.active_from,
                asset.active_to
            FROM cam_tracking_assets AS asset
            INNER JOIN cam_campaigns AS campaign ON campaign.id = asset.campaign_id
            LEFT JOIN cam_channels AS channel ON channel.id = asset.channel_id
            LEFT JOIN cam_sources AS source ON source.id = asset.source_id
            LEFT JOIN cam_mediums AS medium ON medium.id = asset.medium_id
            WHERE asset.tracking_reference = @tracking_reference
            LIMIT 1
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tracking_reference", NpgsqlDbType.Text)
            {
                Value = trackingReference.Trim()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var status = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
            var activeFrom = reader.GetFieldValue<DateTimeOffset>(10);
            var activeTo = reader.GetFieldValue<DateTimeOffset>(11);
            var now = DateTimeOffset.UtcNow;

            // "Active" means BOTH approved-and-live AND inside its window. A poster whose run has
            // ended should not keep attributing gifts to a finished burst of activity, and a link
            // scheduled for next week should not work today.
            var isActive = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                           && activeFrom <= now
                           && activeTo >= now;

            return new TrackingAttribution(
                TenantId: reader.GetGuid(1),
                BusinessUnitId: reader.IsDBNull(2) ? Guid.Empty : reader.GetGuid(2),
                TrackingAssetId: reader.GetGuid(0),
                CampaignId: reader.GetGuid(3),
                CampaignName: reader.GetString(4),
                Channel: reader.IsDBNull(5) ? null : reader.GetString(5),
                Source: reader.IsDBNull(6) ? null : reader.GetString(6),
                Medium: reader.IsDBNull(7) ? null : reader.GetString(7),
                OwnerUserId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
                IsActive: isActive);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not resolve tracking reference {TrackingReference}. The donation will "
                + "proceed unattributed rather than failing.",
                trackingReference);

            return null;
        }
    }

    /// <summary>
    /// Whether a campaign may currently take a donation.
    ///
    /// A CLOSED OR UNAPPROVED CAMPAIGN MUST NOT TAKE MONEY. Accepting a gift against one leaves
    /// income with nowhere legitimate to be reported, and refunding it afterwards is a far worse
    /// experience for the donor than being told at the time.
    ///
    /// PAUSED IS TREATED AS CLOSED FOR DONATIONS, deliberately. Pausing a campaign is an explicit
    /// act by somebody who wanted it to stop; continuing to take money against it would make the
    /// pause meaningless.
    ///
    /// THE REASONS ARE DONOR-FACING. "This campaign has closed" is something a donor can act on;
    /// "campaign status is PendingApproval" is an internal detail that tells them nothing and
    /// leaks how the organisation works.
    /// </summary>
    public async Task<CampaignDonationEligibility> GetDonationEligibilityAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || campaignId == Guid.Empty)
        {
            return CampaignDonationEligibility.NotFound;
        }

        const string Sql = """
            SELECT campaign.name, campaign.status, campaign.start_date, campaign.end_date,
                   currency.code
            FROM cam_campaigns AS campaign
            LEFT JOIN gm_currencies AS currency ON currency.id = campaign.currency_id
            WHERE campaign.tenant_id = @tenant_id AND campaign.id = @campaign_id
            LIMIT 1
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("campaign_id", NpgsqlDbType.Uuid) { Value = campaignId });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return CampaignDonationEligibility.NotFound;
            }

            var name = reader.GetString(0);
            var status = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var endDate = reader.GetFieldValue<DateOnly>(3);
            var currencyCode = reader.IsDBNull(4) ? null : reader.GetString(4);

            var acceptingStatuses = new[] { "Approved", "Scheduled", "Active" };

            if (!acceptingStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new CampaignDonationEligibility(
                    false, name, currencyCode, "This campaign is not currently accepting donations.");
            }

            // The end date is inclusive - a campaign running to the 31st takes gifts all day on
            // the 31st, which is what everybody means by an end date and what the donor on the
            // last evening expects.
            if (endDate < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return new CampaignDonationEligibility(
                    false, name, currencyCode, "This campaign has ended.");
            }

            return new CampaignDonationEligibility(true, name, currencyCode, null);
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not check donation eligibility for campaign {CampaignId}.",
                campaignId);

            // REFUSING IS THE SAFE ANSWER. Taking money against a campaign whose state is unknown
            // is the failure that cannot be undone; asking the donor to try again can be.
            return new CampaignDonationEligibility(
                false, string.Empty, null, "This campaign could not be checked. Please try again.");
        }
    }

    /// <summary>
    /// A command on the SAME connection and transaction the caller is already using, so a read
    /// taken inside a donation transaction sees that transaction's own writes.
    /// </summary>
    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(sql, connection);

        if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
        {
            command.Transaction = transaction;
        }

        return command;
    }
}
