using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Infrastructure.Persistence;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// Money and currency, read from the payments and reference tables over the shared database.
///
/// READ-ONLY, WITHOUT EXCEPTION. CAM never writes a donation, a refund or a currency. That is what
/// makes a seam across a shared database defensible here: the whole surface is queries, and if
/// payments ever move to a database of their own, this one class is what changes.
///
/// THE PAYMENT RULES ARE NOT REPRODUCED HERE. What counts as income, and what a refund or a
/// chargeback does to a total, is PAY's decision - these queries read the state PAY has already
/// written. A second implementation of those rules in CAM would drift from the first within a
/// month, and the two would then disagree about how much a campaign had raised.
///
/// NOTHING HERE THROWS. A campaign register that cannot reach the payment tables should show its
/// campaigns with no income figures, not fail to load. A missing number is visibly missing; a page
/// that will not open tells nobody anything.
/// </summary>
public sealed class FinancialDirectory(
    CampaignDbContext context,
    ILogger<FinancialDirectory> logger) : IFinancialDirectory
{
    /// <summary>
    /// The donation states that count as money the organisation actually holds.
    ///
    /// SETTLED AND RECORDED ONLY, plus partially refunded - whose remaining balance is real income
    /// and is netted below. A fully refunded, charged-back or voided donation is money the
    /// organisation does not have, and counting it toward a campaign's progress would tell a
    /// fundraiser they had hit a target they had not.
    /// </summary>
    private const string ConfirmedStatuses = "('Recorded', 'Settled', 'PartiallyRefunded')";

    public async Task<IReadOnlyDictionary<Guid, string>> GetCurrencyCodesAsync(
        IReadOnlyCollection<Guid> currencyIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currencyIds);

        var codes = new Dictionary<Guid, string>();

        if (currencyIds.Count == 0)
        {
            return codes;
        }

        // NO TENANT FILTER. Currencies are platform reference data - GBP is GBP for everybody - and
        // the ids being resolved came from rows the caller was already allowed to read.
        const string Sql = """
            SELECT id, code
            FROM gm_currencies
            WHERE id = ANY(@ids)
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = currencyIds.Distinct().ToArray()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                codes[reader.GetGuid(0)] = reader.GetString(1).Trim();
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not resolve currency codes. The affected rows will display without one.");
        }

        return codes;
    }

    public async Task<CampaignIncome> GetCampaignIncomeAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken)
    {
        var income = await GetCampaignIncomeAsync(tenantId, [campaignId], cancellationToken);

        return income.TryGetValue(campaignId, out var found)
            ? found
            : new CampaignIncome { CampaignId = campaignId };
    }

    /// <summary>
    /// What several campaigns have raised.
    ///
    /// ONE QUERY FOR THE WHOLE PAGE. A register showing twenty campaigns with a progress bar each
    /// would otherwise issue twenty queries to draw one screen, on the page people open most.
    ///
    /// THE REFUNDED AMOUNT IS SUBTRACTED, not reported alongside. A campaign that raised 10,000 and
    /// refunded 2,000 has 8,000; showing the gross figure against a target would overstate progress
    /// by exactly the amount that went back to donors.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, CampaignIncome>> GetCampaignIncomeAsync(
        Guid tenantId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(campaignIds);

        var income = new Dictionary<Guid, CampaignIncome>();

        if (tenantId == Guid.Empty || campaignIds.Count == 0)
        {
            return income;
        }

        var sql = $"""
            SELECT
                campaign_id,
                COALESCE(SUM(amount), 0) - COALESCE(SUM(refunded_amount), 0) AS confirmed,
                COUNT(*) AS donation_count,
                COUNT(DISTINCT COALESCE(donor_id, id)) AS donor_count,
                COALESCE(SUM(refunded_amount), 0) AS refunded
            FROM pay_donations
            WHERE tenant_id = @tenant_id
              AND campaign_id = ANY(@ids)
              AND status IN {ConfirmedStatuses}
            GROUP BY campaign_id
            """;

        try
        {
            await using var command = await CreateCommandAsync(sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = campaignIds.Distinct().ToArray()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var campaignId = reader.GetGuid(0);

                income[campaignId] = new CampaignIncome
                {
                    CampaignId = campaignId,
                    ConfirmedAmount = reader.GetDecimal(1),
                    DonationCount = reader.GetInt32(2),
                    DonorCount = reader.GetInt32(3),
                    RefundedAmount = reader.GetDecimal(4)
                };
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not read campaign income for organisation {TenantId}. "
                + "The affected rows will display without income figures.",
                tenantId);
        }

        return income;
    }

    /// <summary>
    /// The donations attributed to an organisation.
    ///
    /// THE JOINS ARE LEFT, every one of them. A donation whose tracking asset was later retired, or
    /// whose channel row was removed, is still a donation and must still appear - with blanks where
    /// the attribution used to be. An inner join would make those gifts vanish from the explorer,
    /// which is the one screen somebody would use to find out what happened to them.
    ///
    /// PAGED IN THE DATABASE. An organisation that has been running a year has more donations than
    /// a browser should ever be handed, and this screen is for investigating one gift rather than
    /// for downloading the lot.
    /// </summary>
    public async Task<(IReadOnlyList<AttributedDonation> Items, int TotalCount)>
        SearchAttributedDonationsAsync(
            Guid tenantId,
            Guid? campaignId,
            Guid? trackingAssetId,
            string? search,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            bool? attributedOnly,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var items = new List<AttributedDonation>();

        if (tenantId == Guid.Empty)
        {
            return (items, 0);
        }

        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize is < 1 or > 200 ? 25 : pageSize;

        var predicates = new List<string> { "donation.tenant_id = @tenant_id" };

        if (campaignId is not null)
        {
            predicates.Add("donation.campaign_id = @campaign_id");
        }

        if (trackingAssetId is not null)
        {
            predicates.Add("donation.tracking_asset_id = @tracking_asset_id");
        }

        if (fromUtc is not null)
        {
            predicates.Add("donation.donated_at_utc >= @from_utc");
        }

        if (toUtc is not null)
        {
            predicates.Add("donation.donated_at_utc <= @to_utc");
        }

        if (attributedOnly == true)
        {
            predicates.Add("donation.tracking_asset_id IS NOT NULL");
        }
        else if (attributedOnly == false)
        {
            predicates.Add("donation.tracking_asset_id IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE, and parameterised. The columns are chosen deliberately: a person looking for
            // one gift has either its reference, the donor's name, or the tracking reference from
            // the link they followed.
            predicates.Add("""
                (donation.donation_reference ILIKE @search
                 OR donation.donor_name ILIKE @search
                 OR intent.tracking_reference ILIKE @search)
                """);
        }

        var where = string.Join(" AND ", predicates);

        var sql = $"""
            SELECT
                donation.id,
                donation.donation_reference,
                donation.donated_at_utc,
                donation.amount,
                donation.currency_code,
                donation.status,
                donation.campaign_id,
                COALESCE(campaign.name, ''),
                donation.tracking_asset_id,
                COALESCE(intent.tracking_reference, ''),
                COALESCE(channel.name, ''),
                COALESCE(source.name, ''),
                COALESCE(medium.name, ''),
                donation.donor_name,
                donation.donor_id,
                COUNT(*) OVER () AS total_count
            FROM pay_donations AS donation
            LEFT JOIN pay_donation_intents AS intent ON intent.id = donation.donation_intent_id
            LEFT JOIN cam_campaigns AS campaign ON campaign.id = donation.campaign_id
            LEFT JOIN cam_tracking_assets AS asset ON asset.id = donation.tracking_asset_id
            LEFT JOIN cam_channels AS channel ON channel.id = asset.channel_id
            LEFT JOIN cam_sources AS source ON source.id = asset.source_id
            LEFT JOIN cam_mediums AS medium ON medium.id = asset.medium_id
            WHERE {where}
            ORDER BY donation.donated_at_utc DESC
            LIMIT @limit OFFSET @offset
            """;

        var total = 0;

        try
        {
            await using var command = await CreateCommandAsync(sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = safeSize });
            command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer)
            {
                Value = (safePage - 1) * safeSize
            });

            if (campaignId is { } campaign)
            {
                command.Parameters.Add(new NpgsqlParameter("campaign_id", NpgsqlDbType.Uuid) { Value = campaign });
            }

            if (trackingAssetId is { } asset)
            {
                command.Parameters.Add(new NpgsqlParameter("tracking_asset_id", NpgsqlDbType.Uuid) { Value = asset });
            }

            if (fromUtc is { } from)
            {
                command.Parameters.Add(new NpgsqlParameter("from_utc", NpgsqlDbType.TimestampTz) { Value = from });
            }

            if (toUtc is { } to)
            {
                command.Parameters.Add(new NpgsqlParameter("to_utc", NpgsqlDbType.TimestampTz) { Value = to });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text)
                {
                    Value = $"%{search.Trim()}%"
                });
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var trackingAsset = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);

                items.Add(new AttributedDonation
                {
                    DonationId = reader.GetGuid(0),
                    Reference = reader.GetString(1),
                    ReceivedAtUtc = reader.GetFieldValue<DateTimeOffset>(2),
                    Amount = reader.GetDecimal(3),
                    CurrencyCode = reader.GetString(4).Trim(),
                    Status = reader.GetString(5),
                    CampaignId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    CampaignName = reader.GetString(7),
                    TrackingAssetId = trackingAsset,
                    TrackingReference = reader.GetString(9),
                    ChannelName = reader.GetString(10),
                    SourceName = reader.GetString(11),
                    MediumName = reader.GetString(12),
                    DonorName = reader.GetString(13),
                    DonorId = reader.IsDBNull(14) ? null : reader.GetGuid(14),

                    // ATTRIBUTED MEANS TRACED TO AN ASSET, not merely assigned to a campaign. A
                    // gift recorded against a campaign by hand is attributed to nothing, and a
                    // report that treated the two alike would credit a QR code with money somebody
                    // gave over the telephone.
                    IsAttributed = trackingAsset is not null,
                    HasOpenCorrectionRequest = false
                });

                total = reader.GetInt32(15);
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not read attributed donations for organisation {TenantId}.",
                tenantId);

            return ([], 0);
        }

        return (items, total);
    }

    public async Task<AttributedDonation?> GetAttributedDonationAsync(
        Guid tenantId, Guid donationId, CancellationToken cancellationToken)
    {
        var (items, _) = await SearchAttributedDonationsAsync(
            tenantId, null, null, null, null, null, null, 1, 200, cancellationToken);

        var match = items.FirstOrDefault(donation => donation.DonationId == donationId);

        if (match is not null)
        {
            return match;
        }

        // Not on the first page. Asked for directly rather than paging through, because a donation
        // reference is a name for one record and looking it up should not depend on how recent it is.
        const string Sql = """
            SELECT
                donation.id,
                donation.donation_reference,
                donation.donated_at_utc,
                donation.amount,
                donation.currency_code,
                donation.status,
                donation.campaign_id,
                COALESCE(campaign.name, ''),
                donation.tracking_asset_id,
                COALESCE(intent.tracking_reference, ''),
                COALESCE(channel.name, ''),
                COALESCE(source.name, ''),
                COALESCE(medium.name, ''),
                donation.donor_name,
                donation.donor_id
            FROM pay_donations AS donation
            LEFT JOIN pay_donation_intents AS intent ON intent.id = donation.donation_intent_id
            LEFT JOIN cam_campaigns AS campaign ON campaign.id = donation.campaign_id
            LEFT JOIN cam_tracking_assets AS asset ON asset.id = donation.tracking_asset_id
            LEFT JOIN cam_channels AS channel ON channel.id = asset.channel_id
            LEFT JOIN cam_sources AS source ON source.id = asset.source_id
            LEFT JOIN cam_mediums AS medium ON medium.id = asset.medium_id
            WHERE donation.tenant_id = @tenant_id AND donation.id = @donation_id
            LIMIT 1
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("donation_id", NpgsqlDbType.Uuid) { Value = donationId });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var trackingAsset = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);

            return new AttributedDonation
            {
                DonationId = reader.GetGuid(0),
                Reference = reader.GetString(1),
                ReceivedAtUtc = reader.GetFieldValue<DateTimeOffset>(2),
                Amount = reader.GetDecimal(3),
                CurrencyCode = reader.GetString(4).Trim(),
                Status = reader.GetString(5),
                CampaignId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                CampaignName = reader.GetString(7),
                TrackingAssetId = trackingAsset,
                TrackingReference = reader.GetString(9),
                ChannelName = reader.GetString(10),
                SourceName = reader.GetString(11),
                MediumName = reader.GetString(12),
                DonorName = reader.GetString(13),
                DonorId = reader.IsDBNull(14) ? null : reader.GetGuid(14),
                IsAttributed = trackingAsset is not null,
                HasOpenCorrectionRequest = false
            };
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not read donation {DonationId} for organisation {TenantId}.",
                donationId, tenantId);

            return null;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, CampaignIncome>> GetTrackingAssetIncomeAsync(
        Guid tenantId, IReadOnlyCollection<Guid> trackingAssetIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackingAssetIds);

        var income = new Dictionary<Guid, CampaignIncome>();

        if (tenantId == Guid.Empty || trackingAssetIds.Count == 0)
        {
            return income;
        }

        var sql = $"""
            SELECT
                tracking_asset_id,
                COALESCE(SUM(amount), 0) - COALESCE(SUM(refunded_amount), 0) AS confirmed,
                COUNT(*) AS donation_count,
                COUNT(DISTINCT COALESCE(donor_id, id)) AS donor_count,
                COALESCE(SUM(refunded_amount), 0) AS refunded
            FROM pay_donations
            WHERE tenant_id = @tenant_id
              AND tracking_asset_id = ANY(@ids)
              AND status IN {ConfirmedStatuses}
            GROUP BY tracking_asset_id
            """;

        try
        {
            await using var command = await CreateCommandAsync(sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = trackingAssetIds.Distinct().ToArray()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var assetId = reader.GetGuid(0);

                income[assetId] = new CampaignIncome
                {
                    CampaignId = assetId,
                    ConfirmedAmount = reader.GetDecimal(1),
                    DonationCount = reader.GetInt32(2),
                    DonorCount = reader.GetInt32(3),
                    RefundedAmount = reader.GetDecimal(4)
                };
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not read tracking asset income for organisation {TenantId}.",
                tenantId);
        }

        return income;
    }

    /// <summary>
    /// A command on the DbContext's own connection, inside its transaction when there is one.
    ///
    /// SHARING THE CONNECTION IS THE POINT. A separate connection would read outside any
    /// transaction in progress, so a figure read here would not see writes the same request had
    /// already made - and the page would show two different answers depending on the order things
    /// happened to run in.
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
