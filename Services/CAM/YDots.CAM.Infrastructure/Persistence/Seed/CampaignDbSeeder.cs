using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Domain.Common.Enums;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

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
/// ONE CAMPAIGN IS SEEDED AGAIN, AND THE DIFFERENCE FROM WHAT WAS REMOVED IS THE ORGANISATION.
/// It is stamped with <c>SeedSettings:OrganisationId</c> - the same value IAM gives the activated
/// sample Organisation and DON stamps its own demonstration data with - so a real administrator's
/// token carries the Organisation the row belongs to and the campaign is visible where the old
/// fabricated one was not. It exists because a fresh database otherwise has no campaign at all:
/// the donation form's picker is empty, no tracking asset can be generated, and nothing
/// downstream of a campaign can be exercised until somebody drives the creation wizard by hand.
///
/// IT IS APPROVED AND FULLY READY, deliberately. An approved campaign with every required
/// readiness check passed is the only state from which the whole donation flow can be walked -
/// the picker offers only campaigns that have passed approval, and the launch gate refuses one
/// with an outstanding required check.
///
/// IT IS IDEMPOTENT. Each row is checked by its fixed id before insert, so the seeder runs on
/// every start without duplicating anything and a reference row added in a later release
/// reaches an existing database by deploying and restarting.
///
/// THE IDS CARRY OVER UNCHANGED from the previous seed data, so an existing CAM database keeps
/// working: the rows it already has are recognised rather than duplicated.
/// </summary>
public sealed class CampaignDbSeeder(
    CampaignDbContext context,
    IOptions<SeedSettings> seedOptions,
    ITrackingReferenceGenerator references,
    ILogger<CampaignDbSeeder> logger)
{
    private readonly SeedSettings _seed = seedOptions.Value;

    // =============================================================================================
    // The demonstration campaign
    // =============================================================================================

    /// <summary>
    /// The campaign's fixed id, which is what makes this seeder idempotent.
    ///
    /// A FIXED ID RATHER THAN A LOOKUP BY CODE, matching every other row in this file. The seeder
    /// runs on every start; recognising its own row by id means it inserts once and then does
    /// nothing, and an administrator who renames or re-codes the campaign keeps their version.
    /// </summary>
    private static readonly Guid SampleCampaignId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Indian Rupee and India, from IAM's global master catalogue.
    ///
    /// HARD-CODED IDS, AND SAFE TO BE. These are seed constants in IAM's GlobalMasterSeeder, in
    /// the same style as the channel, source and medium ids above, and the campaign columns that
    /// hold them are plain Guids rather than foreign keys - so the two services can seed in any
    /// order and neither waits for the other.
    /// </summary>
    private static readonly Guid InrCurrencyId =
        Guid.Parse("55555555-5555-5555-5555-555555555001");

    private static readonly Guid IndiaCountryId =
        Guid.Parse("11111111-1111-1111-1111-111111111001");

    /// <summary>The channels the demonstration campaign runs on.</summary>
    private static readonly Guid WebsiteChannelId =
        Guid.Parse("10000000-0000-0000-0000-000000000005");

    /// <summary>
    /// Offline, which is the one a QR poster needs.
    ///
    /// IT IS HERE ON PURPOSE. A tracking asset on this channel is the printed-poster case the
    /// donation flow is built around, and a campaign that does not carry the channel cannot have
    /// one generated against it.
    /// </summary>
    private static readonly Guid OfflineChannelId =
        Guid.Parse("10000000-0000-0000-0000-000000000008");

    /// <summary>
    /// The pre-launch checklist, one item per category, every one required and every one passed.
    ///
    /// ALL SIX CATEGORIES ARE COVERED because the checklist screen groups by them, and a
    /// demonstration campaign that filled three of six would look like a half-finished record
    /// rather than a ready one. `RequiredForLaunch` is true on all of them: an optional check
    /// that passes proves nothing about whether the launch gate works.
    /// </summary>
    private static readonly (string Id, string Name, ReadinessCheckCategory Category,
        string Criteria, string Description)[] ReadinessChecks =
    [
        ("41000000-0000-0000-0000-000000000001", "Public appeal copy approved",
            ReadinessCheckCategory.Content,
            "The public description and terms have been reviewed and signed off.",
            "The wording a donor reads on the giving page, checked before anybody sees it."),

        ("41000000-0000-0000-0000-000000000002", "Budget and target agreed",
            ReadinessCheckCategory.Budget,
            "The target amount and budget are approved and within the annual plan.",
            "What the campaign is asking for and what it is allowed to spend getting there."),

        ("41000000-0000-0000-0000-000000000003", "Tracking assets generated",
            ReadinessCheckCategory.Tracking,
            "At least one tracking asset exists and resolves to the public donation form.",
            "The QR codes and links that make a gift attributable to this campaign."),

        ("41000000-0000-0000-0000-000000000004", "Payment gateway configured",
            ReadinessCheckCategory.Payment,
            "The organisation has an active payment gateway configuration in the settlement currency.",
            "Where the money actually lands. Checked before the first donor is invited to give."),

        ("41000000-0000-0000-0000-000000000005", "Receipt template ready",
            ReadinessCheckCategory.Template,
            "The receipt template renders and names the campaign correctly.",
            "The tax receipt a donor claims relief with, proved before it is issued in anger."),

        ("41000000-0000-0000-0000-000000000006", "Consent wording current",
            ReadinessCheckCategory.Consent,
            "The consent text on the donation form names the current privacy notice version.",
            "What the donor agrees to when they give, and the version it is recorded against.")
    ];

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

    /// <summary>
    /// The demonstration campaign's tracking assets.
    ///
    /// WHY THE MANAGER NEEDED THEM. The campaign seed put a campaign on a fresh database and
    /// stopped there, so the tracking asset manager opened on its own empty state: no rows, no
    /// counts, nothing for the QR preview, the placement table or the disable-request flow to be
    /// exercised against. Every one of those had to be reached by driving the Generate form by
    /// hand first, which is the same gap the campaign seed exists to close one level up.
    ///
    /// FIVE ASSETS ACROSS FOUR STATES AND ALL FOUR TYPES, because the screen's whole vocabulary
    /// is status and type: Draft has an Edit and a Submit, Submitted has an Approve, Approved has
    /// an Activate, and only Active has a live URL, a usage count and a Request disable. A set
    /// that was uniformly Active would leave four of those five buttons unreachable.
    ///
    /// THE OFFLINE QR CODES CARRY PLACEMENTS AND NOTHING ELSE DOES. `ValidatePlacements` refuses
    /// both directions - an offline QR code with no place, and a place on anything else - so a
    /// seeded row that ignored the rule would be a record the product itself would not accept.
    ///
    /// THE DATES ARE RELATIVE TO NOW, for the same reason the campaign's are. `IsLiveAt` checks
    /// the window as well as the status, so an asset seeded with a window from the day this was
    /// written would report as not live on every database created afterwards.
    /// </summary>
    private static readonly (string Id, string Suffix, TrackingAssetType Type, string ChannelId,
        string SourceId, string MediumId, string Destination, string? ContentTag,
        TrackingAssetStatus Status, long Usage, decimal Received, string Reference)[] TrackingAssets =
    [
        // ---- ACTIVE: THE PRINTED POSTER ---------------------------------------------------
        //
        // The case the whole attribution chain is built around. It carries placements, a usage
        // count and money against it, so the manager's tiles and the place-level breakdown have
        // something real to total.
        ("44000000-0000-0000-0000-000000000001", "QR-001", TrackingAssetType.QRCode,
            "10000000-0000-0000-0000-000000000008", "20000000-0000-0000-0000-000000000007",
            "30000000-0000-0000-0000-000000000008",
            "https://give.ngoplanet.com/donate", "annual-appeal-poster",
            TrackingAssetStatus.Active, 412, 186_500m, "QR7K3MNPB49F"),

        // ---- ACTIVE: THE EVENT QR CODE ----------------------------------------------------
        //
        // A second offline QR code, so the manager shows more than one row that groups by place
        // and the "which poster worked" comparison the reporting exists for is answerable.
        ("44000000-0000-0000-0000-000000000002", "QR-002", TrackingAssetType.QRCode,
            "10000000-0000-0000-0000-000000000008", "20000000-0000-0000-0000-000000000009",
            "30000000-0000-0000-0000-000000000008",
            "https://give.ngoplanet.com/donate", "founders-day-stall",
            TrackingAssetStatus.Active, 97, 43_250m, "QRW8XCTJ2H6D"),

        // ---- ACTIVE: THE NEWSLETTER LINK --------------------------------------------------
        //
        // A short link on e-mail. No placements, deliberately: a link has no physical location,
        // which is the distinction `HasPlacements` draws.
        ("44000000-0000-0000-0000-000000000003", "SL-001", TrackingAssetType.ShortLink,
            "10000000-0000-0000-0000-000000000004", "20000000-0000-0000-0000-000000000001",
            "30000000-0000-0000-0000-000000000001",
            "https://give.ngoplanet.com/donate", "march-newsletter",
            TrackingAssetStatus.Active, 268, 91_800m, "SLQ4RBMK7NVZ"),

        // ---- APPROVED, NOT YET LIVE -------------------------------------------------------
        //
        // Approved and waiting to be activated, which is the one state the Activate button can
        // be exercised from. It has no tracking reference and no URL yet - both are minted at
        // activation, and writing them onto an unactivated row would misrepresent what the
        // product does.
        ("44000000-0000-0000-0000-000000000004", "UTM-001", TrackingAssetType.UTMLink,
            "10000000-0000-0000-0000-000000000005", "20000000-0000-0000-0000-000000000006",
            "30000000-0000-0000-0000-000000000003",
            "https://hopefoundation.example.org/annual-giving", "homepage-hero",
            TrackingAssetStatus.Approved, 0, 0m, ""),

        // ---- DRAFT ------------------------------------------------------------------------
        //
        // Still being written, so Edit, Submit and Delete draft are all reachable from the first
        // start rather than only after somebody has created a row by hand.
        ("44000000-0000-0000-0000-000000000005", "LP-001", TrackingAssetType.LandingPage,
            "10000000-0000-0000-0000-000000000001", "20000000-0000-0000-0000-000000000003",
            "30000000-0000-0000-0000-000000000005",
            "https://hopefoundation.example.org/give/instagram", "reels-campaign",
            TrackingAssetStatus.Draft, 0, 0m, "")
    ];

    /// <summary>
    /// Where the two offline QR codes are physically posted.
    ///
    /// THE PLACE IS WHAT MAKES AN OFFLINE QR CODE WORTH TRACKING. One code printed in six halls
    /// reports as one row without these, and the question the module exists to answer - which
    /// placement produced the gift - has nothing to group by.
    ///
    /// CITY AND STATE ARE LEFT NULL, matching what the product stores for this campaign: the
    /// create path fills them from the campaign's own geography, and the seeded campaign carries
    /// none. Inventing ids here would put geography on the record that the campaign cannot
    /// account for.
    /// </summary>
    private static readonly (string Id, string AssetId, string PlaceName, string Destination)[]
        TrackingAssetPlaces =
    [
        ("45000000-0000-0000-0000-000000000001", "44000000-0000-0000-0000-000000000001",
            "Community centre notice board", "https://give.ngoplanet.com/donate"),
        ("45000000-0000-0000-0000-000000000002", "44000000-0000-0000-0000-000000000001",
            "Railway station concourse", "https://give.ngoplanet.com/donate"),
        ("45000000-0000-0000-0000-000000000003", "44000000-0000-0000-0000-000000000002",
            "Founders day stall, main hall", "https://give.ngoplanet.com/donate")
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

        // AFTER THE REFERENCE DATA AND IN ITS OWN SAVE. The campaign names two channels, so the
        // channel rows have to exist first - and keeping the saves separate means a failure while
        // inserting the campaign cannot roll back the reference data the module needs to function.
        await SeedSampleCampaignAsync(cancellationToken);

        // LAST, AND AS A STEP OF ITS OWN RATHER THAN INSIDE THE CAMPAIGN INSERT. The campaign
        // seed returns early the moment its row exists, so anything nested in it can only ever
        // reach a database that has never been seeded - and the tracking assets were added after
        // the campaign shipped. Run from here they reconcile on every start, so an existing
        // database gets them by deploying rather than by being dropped.
        await SeedTrackingAssetsAsync(cancellationToken);
    }

    /// <summary>
    /// The demonstration campaign's tracking assets, and the placements on the offline ones.
    ///
    /// IDEMPOTENT BY FIXED ID, like every other row in this file: the assets already present are
    /// read once and skipped, so this runs on every start and inserts each row exactly once. An
    /// operator who edits, activates or takes one down keeps their version.
    ///
    /// IT NEEDS THE CAMPAIGN. Every asset hangs off <see cref="SampleCampaignId"/> by foreign
    /// key, so a database where the campaign was never seeded - <c>CreateSampleData</c> off, or
    /// the campaign deleted - gets nothing here rather than a failed insert.
    /// </summary>
    private async Task SeedTrackingAssetsAsync(CancellationToken cancellationToken)
    {
        if (!_seed.CreateSampleData || _seed.OrganisationId == Guid.Empty)
        {
            return;
        }

        var campaign = await context.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == SampleCampaignId)
            .Select(item => new { item.Id, item.Code, item.TenantId, item.BusinessUnitId })
            .FirstOrDefaultAsync(cancellationToken);

        if (campaign is null)
        {
            return;
        }

        var existing = await context.TrackingAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(asset => asset.CampaignId == SampleCampaignId)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken);

        var present = existing.ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        // The codes each asset's URL carries, read once. `BuildUrl` writes them into the query
        // string as utm_source and utm_medium, and a row per asset would be three round trips
        // apiece for two tables of nine.
        var sourceCodes = await context.Sources
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Code, cancellationToken);

        var mediumCodes = await context.Mediums
            .AsNoTracking()
            .ToDictionaryAsync(medium => medium.Id, medium => medium.Code, cancellationToken);

        foreach (var seed in TrackingAssets)
        {
            var id = Guid.Parse(seed.Id);

            if (present.Contains(id))
            {
                continue;
            }

            var isLive = seed.Status == TrackingAssetStatus.Active;

            var asset = new TrackingAsset
            {
                Id = id,
                TenantId = campaign.TenantId,
                BusinessUnitId = campaign.BusinessUnitId,
                CampaignId = campaign.Id,

                // The same shape the product mints: CMP-SEED-0001-QR-001.
                Code = $"{campaign.Code}-{seed.Suffix}",

                AssetType = seed.Type,
                ChannelId = Guid.Parse(seed.ChannelId),
                SourceId = Guid.Parse(seed.SourceId),
                MediumId = Guid.Parse(seed.MediumId),
                Destination = seed.Destination,
                ContentTag = seed.ContentTag,
                Status = seed.Status,

                // YESTERDAY TO A YEAR OUT, so an Active asset is inside its own window the moment
                // it is seeded. `IsLiveAt` checks the window as well as the status, so a row that
                // says Active outside its dates resolves nothing and reads as a broken seed.
                ActiveFrom = now.AddDays(-1),
                ActiveTo = now.AddYears(1),

                // ONLY ON A LIVE ASSET. The reference is minted at activation and the URL is
                // built from it, so a Draft or Approved row carries neither - see the note on
                // the blueprint above.
                TrackingReference = isLive ? seed.Reference : null,

                UsageCount = seed.Usage,
                TotalReceived = seed.Received,

                CreatedAtUtc = now,
                CreatedByUserId = _seed.SystemUserId,
                Version = 1
            };

            // SUBMITTED AND APPROVED ARE STAMPED ON EVERYTHING PAST DRAFT, because the record has
            // to explain itself: an approved asset with no approver and no date is the shape of a
            // row somebody forged, and the manager shows both.
            if (seed.Status != TrackingAssetStatus.Draft)
            {
                asset.SubmittedByUserId = _seed.SystemUserId;
                asset.SubmittedAtUtc = now;
                asset.ApprovedByUserId = _seed.SystemUserId;
                asset.ApprovedAtUtc = now;
            }

            if (isLive)
            {
                asset.GeneratedUrl = references.BuildUrl(
                    asset,
                    sourceCodes.GetValueOrDefault(asset.SourceId) ?? string.Empty,
                    mediumCodes.GetValueOrDefault(asset.MediumId) ?? string.Empty,
                    campaign.Code);
            }

            foreach (var place in TrackingAssetPlaces.Where(item => item.AssetId == seed.Id))
            {
                asset.Places.Add(new TrackingAssetPlace
                {
                    Id = Guid.Parse(place.Id),
                    TrackingAssetId = asset.Id,
                    PlaceName = place.PlaceName,
                    Destination = place.Destination,
                    CreatedAtUtc = now,
                    CreatedByUserId = _seed.SystemUserId,
                    Version = 1
                });
            }

            await context.TrackingAssets.AddAsync(asset, cancellationToken);
            added++;
        }

        if (added == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Count} tracking asset(s) for campaign {Code}.", added, campaign.Code);
    }

    /// <summary>
    /// One approved, fully ready campaign for the seeded Organisation.
    ///
    /// IDEMPOTENT BY ID. The row is recognised on every start after the first, so this runs on
    /// every boot and inserts once - and an administrator who edits, renames or closes it keeps
    /// their version untouched.
    ///
    /// THE DATES ARE RELATIVE TO NOW, not fixed. A campaign seeded with dates from the day the
    /// code was written is expired on any database created afterwards, and an expired campaign is
    /// not offered on the donation form - which would make this seed useless for the exact
    /// purpose it exists for. It starts yesterday and runs for a year.
    /// </summary>
    private async Task SeedSampleCampaignAsync(CancellationToken cancellationToken)
    {
        if (!_seed.CreateSampleData || _seed.OrganisationId == Guid.Empty)
        {
            return;
        }

        var exists = await context.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(campaign => campaign.Id == SampleCampaignId, cancellationToken);

        if (exists)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var campaign = new Campaign
        {
            Id = SampleCampaignId,
            TenantId = _seed.OrganisationId,
            BusinessUnitId = await ResolveBusinessUnitIdAsync(cancellationToken),
            Code = "CMP-SEED-0001",
            Name = "Hope Foundation Annual Giving",
            Purpose = "General fundraising for the foundation's annual programme of work.",
            FundOrProgramme = "General Fund",

            // YESTERDAY, so the campaign is inside its own window the moment it is seeded rather
            // than a day away from being donatable.
            StartDate = today.AddDays(-1),
            EndDate = today.AddYears(1),

            TargetAmount = 1_000_000m,
            CurrencyId = InrCurrencyId,
            BudgetAmount = 100_000m,
            CountryId = IndiaCountryId,

            PublicDescription =
                "Your gift funds the foundation's work through the year - education, nutrition "
                + "and healthcare for the communities we serve.",
            TermsAndNotice =
                "Donations are eligible for tax relief under section 80G. A receipt is e-mailed "
                + "as soon as your payment is confirmed.",

            // MANUAL, so the campaign sits at Approved and waits to be launched by a person.
            // Automatic activation would move it to Active on its start date and take the
            // Approved state - the one the picker and the launch gate are worth testing against -
            // off the board within a day.
            LifecycleActivation = LifecycleActivation.Manual,
            DaysBeforeStart = 7,
            ReminderTime = new TimeOnly(9, 0),

            Status = CampaignStatus.Approved,
            SubmittedByUserId = _seed.SystemUserId,
            SubmittedAtUtc = now,
            ApprovedByUserId = _seed.SystemUserId,
            ApprovedAtUtc = now,

            CreatedAtUtc = now,
            CreatedByUserId = _seed.SystemUserId,
            Version = 1
        };

        // NO TenantId ON THE OWNER OR THE CHANNEL. Neither is Tenant-owned: both are reachable
        // only through their campaign, which is filtered, so a row here cannot be read except
        // through a campaign the caller was already entitled to. The owner is an AuditEntity, so
        // it records who assigned it; the channel is a pure join row and takes no audit columns.
        campaign.Owners.Add(new CampaignOwner
        {
            Id = Guid.Parse("42000000-0000-0000-0000-000000000001"),
            CampaignId = campaign.Id,
            OwnerId = _seed.SystemUserId,
            IsPrimary = true,
            CreatedAtUtc = now,
            CreatedByUserId = _seed.SystemUserId,
            Version = 1
        });

        foreach (var (channelId, index) in new[] { WebsiteChannelId, OfflineChannelId }.Select(
                     (value, index) => (value, index)))
        {
            campaign.Channels.Add(new CampaignChannel
            {
                Id = Guid.Parse($"43000000-0000-0000-0000-00000000000{index + 1}"),
                CampaignId = campaign.Id,
                ChannelId = channelId
            });
        }

        foreach (var check in ReadinessChecks)
        {
            campaign.ReadinessChecks.Add(new CampaignReadinessCheck
            {
                Id = Guid.Parse(check.Id),
                TenantId = campaign.TenantId,
                BusinessUnitId = campaign.BusinessUnitId,
                CampaignId = campaign.Id,
                CheckName = check.Name,
                Description = check.Description,
                Category = check.Category,
                SuccessCriteria = check.Criteria,
                RequiredForLaunch = true,

                // PASSED, WITH AN OWNER AND A DATE. A check that is merely marked passed with no
                // trace of who passed it reads as data somebody forged; the seeded system user is
                // the honest answer for a seeded row.
                Status = ReadinessCheckStatus.Passed,
                OwnerUserId = _seed.SystemUserId,
                DueDate = today,
                Notes = $"Confirmed by {_seed.SystemUserDisplayName} during setup.",

                CreatedAtUtc = now,
                CreatedByUserId = _seed.SystemUserId,
                Version = 1
            });
        }

        await context.Campaigns.AddAsync(campaign, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded the demonstration campaign {Code} ({Name}) for organisation {OrganisationId}: "
            + "{Status}, {ChannelCount} channel(s), {CheckCount} readiness check(s), all passed.",
            campaign.Code,
            campaign.Name,
            campaign.TenantId,
            campaign.Status,
            campaign.Channels.Count,
            campaign.ReadinessChecks.Count);
    }

    /// <summary>
    /// The seeded Organisation's business unit, read from IAM's own table.
    ///
    /// WHY IT IS READ RATHER THAN CONFIGURED. IAM generates the business unit's id at creation
    /// and nothing publishes it, so there is no value to agree on the way the Organisation id is
    /// agreed. The four services share one database, so this is a local read of one column.
    ///
    /// EMPTY IS AN ACCEPTABLE ANSWER. The Organisation filter on every query is TenantId alone -
    /// BusinessUnitId narrows nothing - so a campaign seeded before IAM has created its tenant row
    /// is still visible to the right people. The column is a stamp, not a boundary.
    /// </summary>
    private async Task<Guid> ResolveBusinessUnitIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();

            command.CommandText =
                "SELECT business_unit_id FROM iam_tenants WHERE id = @tenantId LIMIT 1";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "tenantId";
            parameter.Value = _seed.OrganisationId;
            command.Parameters.Add(parameter);

            await context.Database.OpenConnectionAsync(cancellationToken);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is Guid businessUnitId ? businessUnitId : Guid.Empty;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The seeded Organisation's business unit could not be read. The demonstration "
                + "campaign is stamped without one, which affects nothing that is filtered.");

            return Guid.Empty;
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
