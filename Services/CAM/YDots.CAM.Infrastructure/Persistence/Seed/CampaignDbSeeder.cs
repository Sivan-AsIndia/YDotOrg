using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDots.CAM.Domain.Common.Enums;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the three GLOBAL reference tables: Channel, Source and Medium.
///
/// WHAT IS NO LONGER SEEDED, AND WHY. The previous seeder also inserted sample campaigns,
/// campaign owners, campaign channels, tracking assets and readiness checks - all of them
/// stamped with a hard-coded <c>OrganisationId</c> of
/// <c>00000000-0000-0000-0000-000000000011</c>, matching the fake Organisation the stubbed
/// <c>CurrentUser</c> returned. With real tenancy in place that Organisation does not exist,
/// so those rows would be invisible to every genuine caller: present in the database, returned
/// by nothing, and confusing to the first person who went looking for them.
///
/// Sample campaign data belongs to a demo fixture keyed on a real seeded Organisation, not to
/// the startup path of the production service. What remains here is the reference data the
/// module genuinely cannot function without - a tracking asset cannot be created without a
/// channel, a source and a medium to name.
///
/// IT IS IDEMPOTENT. Each row is checked by its fixed id before insert, so the seeder runs on
/// every start without duplicating anything and a reference row added in a later release
/// reaches an existing database by deploying and restarting.
///
/// THE IDS CARRY OVER UNCHANGED from the previous seed data, so an existing CAM database keeps
/// working: the rows it already has are recognised rather than duplicated.
/// </summary>
public sealed class CampaignDbSeeder(CampaignDbContext context, ILogger<CampaignDbSeeder> logger)
{
    /// <summary>
    /// The channels a campaign can run on.
    ///
    /// OFFLINE IS LOAD-BEARING. <c>TrackingAssetCommandHandler</c> keys the placement rule on
    /// the code <c>OFFLINE</c> - placements are required for it and forbidden for every other
    /// channel - so this row must exist with that exact code. It used to be identified by its
    /// seed GUID, which meant a reseed with fresh ids silently disabled the rule.
    /// </summary>
    private static readonly (string Id, string Code, string Name, string Description, int Order)[] Channels =
    [
        ("10000000-0000-0000-0000-000000000001", "SOCIAL", "Social Media",
            "Campaign activities delivered through social media platforms.", 10),
        ("10000000-0000-0000-0000-000000000002", "MESSAGING", "Messaging",
            "Campaign activities delivered through messaging platforms.", 20),
        ("10000000-0000-0000-0000-000000000003", "SEARCH", "Search",
            "Paid and organic search placements.", 30),
        ("10000000-0000-0000-0000-000000000004", "EMAIL", "Email",
            "Campaign activities delivered by e-mail.", 40),
        ("10000000-0000-0000-0000-000000000005", "WEBSITE", "Website",
            "Placements on the organisation's own website.", 50),
        ("10000000-0000-0000-0000-000000000006", "DIRECT", "Direct",
            "Traffic arriving with no referrer.", 60),
        ("10000000-0000-0000-0000-000000000007", "REFERRAL", "Referral",
            "Traffic arriving from another site.", 70),
        ("10000000-0000-0000-0000-000000000008", "OFFLINE", "Offline",
            "Printed and in-person placements. Tracking assets on this channel carry placements.", 80),
        ("10000000-0000-0000-0000-000000000009", "PARTNER", "Partner",
            "Placements run by a partner organisation.", 90)
    ];

    /// <summary>Where a visitor came from, in the UTM sense.</summary>
    private static readonly (string Id, string Code, string Name, string Description, int Order)[] Sources =
    [
        ("20000000-0000-0000-0000-000000000001", "NEWSLETTER", "Newsletter",
            "The organisation's own mailing list.", 10),
        ("20000000-0000-0000-0000-000000000002", "FACEBOOK", "Facebook", "Facebook placements.", 20),
        ("20000000-0000-0000-0000-000000000003", "INSTAGRAM", "Instagram", "Instagram placements.", 30),
        ("20000000-0000-0000-0000-000000000004", "WHATSAPP", "WhatsApp", "WhatsApp broadcasts.", 40),
        ("20000000-0000-0000-0000-000000000005", "GOOGLE", "Google", "Google search and display.", 50),
        ("20000000-0000-0000-0000-000000000006", "WEBSITE", "Website",
            "The organisation's own website.", 60),
        ("20000000-0000-0000-0000-000000000007", "QR_POSTER", "QR Poster",
            "Printed posters carrying a QR code.", 70),
        ("20000000-0000-0000-0000-000000000008", "PARTNER_SITE", "Partner Site",
            "A partner organisation's website.", 80),
        ("20000000-0000-0000-0000-000000000009", "EVENT", "Event",
            "In-person events and collections.", 90)
    ];

    /// <summary>How a visitor arrived, in the UTM sense.</summary>
    private static readonly (string Id, string Code, string Name, string Description, int Order)[] Mediums =
    [
        ("30000000-0000-0000-0000-000000000001", "EMAIL", "Email", "Delivered in an e-mail.", 10),
        ("30000000-0000-0000-0000-000000000002", "CPC", "Paid Search",
            "Cost-per-click search advertising.", 20),
        ("30000000-0000-0000-0000-000000000003", "ORGANIC", "Organic",
            "Unpaid search results.", 30),
        ("30000000-0000-0000-0000-000000000004", "SOCIAL", "Social",
            "Unpaid social posts.", 40),
        ("30000000-0000-0000-0000-000000000005", "PAID_SOCIAL", "Paid Social",
            "Paid social placements.", 50),
        ("30000000-0000-0000-0000-000000000006", "REFERRAL", "Referral",
            "A link from another site.", 60),
        ("30000000-0000-0000-0000-000000000007", "BANNER", "Banner",
            "Display banner advertising.", 70),
        ("30000000-0000-0000-0000-000000000008", "PRINT", "Print",
            "Printed material.", 80),
        ("30000000-0000-0000-0000-000000000009", "SMS", "SMS", "Text message.", 90)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var added = await SeedChannelsAsync(cancellationToken);
        added += await SeedSourcesAsync(cancellationToken);
        added += await SeedMediumsAsync(cancellationToken);

        if (added > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} campaign reference row(s).", added);
        }
    }

    private async Task<int> SeedChannelsAsync(CancellationToken cancellationToken)
    {
        var existing = await ExistingIdsAsync(context.Channels, cancellationToken);
        var added = 0;

        foreach (var seed in Channels)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Channels.AddAsync(
                new Channel
                {
                    Id = id,
                    Code = seed.Code,
                    Name = seed.Name,
                    Description = seed.Description,
                    Status = Status.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    private async Task<int> SeedSourcesAsync(CancellationToken cancellationToken)
    {
        var existing = await ExistingIdsAsync(context.Sources, cancellationToken);
        var added = 0;

        foreach (var seed in Sources)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Sources.AddAsync(
                new Source
                {
                    Id = id,
                    Code = seed.Code,
                    Name = seed.Name,
                    Description = seed.Description,
                    Status = Status.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    private async Task<int> SeedMediumsAsync(CancellationToken cancellationToken)
    {
        var existing = await ExistingIdsAsync(context.Mediums, cancellationToken);
        var added = 0;

        foreach (var seed in Mediums)
        {
            var id = Guid.Parse(seed.Id);

            if (existing.Contains(id))
            {
                continue;
            }

            await context.Mediums.AddAsync(
                new Medium
                {
                    Id = id,
                    Code = seed.Code,
                    Name = seed.Name,
                    Description = seed.Description,
                    Status = Status.Active,
                    SortOrder = seed.Order,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = Guid.Empty,
                    Version = 1
                },
                cancellationToken);

            added++;
        }

        return added;
    }

    /// <summary>
    /// The ids already present, read once per table.
    ///
    /// One read and a set lookup rather than an EXISTS query per seed row: nine round trips per
    /// table becomes one, and the seeder runs on every application start.
    /// </summary>
    private static async Task<HashSet<Guid>> ExistingIdsAsync<TEntity>(
        DbSet<TEntity> set, CancellationToken cancellationToken)
        where TEntity : Domain.Common.BaseEntity =>
        [.. await set.AsNoTracking().Select(entity => entity.Id).ToListAsync(cancellationToken)];
}
