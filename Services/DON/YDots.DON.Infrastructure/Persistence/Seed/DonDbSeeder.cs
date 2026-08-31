using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Domain.Common;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Infrastructure.Persistence.Seed;

/// <summary>
/// Inserts the reference data and a small demonstration set on first start.
///
/// Everything here is idempotent: the seeder checks before it writes, so restarting the
/// container does not produce a second copy of anything. The organisation id matches the one
/// IAM seeds, otherwise the demo records would sit outside every real user's data scope and
/// nobody would be able to see them.
/// </summary>
public sealed class DonDbSeeder(
    DonDbContext context,
    IOptions<DonorSettings> donorSettings,
    IOptions<SeedSettings> seedSettings,
    ILogger<DonDbSeeder> logger)
{
    private readonly DonorSettings _settings = donorSettings.Value;
    private readonly SeedSettings _seed = seedSettings.Value;

    /// <summary>The YDot organisation id seeded by IAM (SeedSettings:OrganisationId).</summary>
    private Guid OrganisationId => _seed.OrganisationId;

    /// <summary>The IAM system user, used as the actor on every seeded row.</summary>
    private Guid SystemUserId => _seed.SystemUserId;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedCampaignsAsync(cancellationToken);

        if (_seed.CreateSampleData)
        {
            await SeedDonorsAsync(cancellationToken);
            await SeedLeadsAsync(cancellationToken);
        }

        logger.LogInformation("Donors reference and demonstration data is in place.");
    }

    /// <summary>
    /// Has this Organisation already been seeded with rows of this kind?
    ///
    /// IT IGNORES THE QUERY FILTER, and that is the whole point of it existing.
    /// <see cref="OrganisationQueryFilter"/> narrows every <see cref="IOrganisationOwned"/> read
    /// to <c>OrganisationId == TenantContext.OrganisationId</c>, and the seeder runs at STARTUP -
    /// outside any request, with no token and therefore no resolved Organisation. The comparison
    /// is then <c>organisation_id = NULL</c>, which is never true, so a plain guard reports "not
    /// seeded" on every boot however many rows are already there.
    ///
    /// What followed was not a silent no-op: the guard passed, the seeder inserted, and the
    /// unique index on (organisation_id, code) rejected the duplicate, so EVERY restart after
    /// the first failed seeding with
    /// <c>23505: duplicate key value violates unique constraint "ix_don_campaigns_org_code"</c>
    /// and abandoned the donors and leads that would have been seeded after the campaigns.
    ///
    /// Bypassing the filter is safe HERE precisely because there is no caller to isolate from:
    /// the seeder is not serving anybody, and it filters on <see cref="OrganisationId"/>
    /// explicitly instead. Nothing else in this module may do the same.
    /// </summary>
    private Task<bool> AlreadySeededAsync<TEntity>(
        DbSet<TEntity> entities, CancellationToken cancellationToken)
        where TEntity : class, IOrganisationOwned =>
        entities
            .IgnoreQueryFilters()
            .AnyAsync(entity => entity.OrganisationId == OrganisationId, cancellationToken);

    private async Task SeedCampaignsAsync(CancellationToken cancellationToken)
    {
        if (await AlreadySeededAsync(context.Campaigns, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        context.Campaigns.AddRange(
            new Campaign
            {
                OrganisationId = OrganisationId,
                Code = "CMP-2026-GENERAL",
                Name = "General fundraising 2026",
                Description = "The always-on appeal that unattributed leads are recorded against.",
                Status = CampaignStatus.Active,
                StartsAtUtc = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CreatedByUserId = SystemUserId
            },
            new Campaign
            {
                OrganisationId = OrganisationId,
                Code = "CMP-2026-EDUCATION",
                Name = "Education for every child",
                Description = "School fees and materials appeal.",
                Status = CampaignStatus.Active,
                StartsAtUtc = new DateTimeOffset(now.Year, 4, 1, 0, 0, 0, TimeSpan.Zero),
                EndsAtUtc = new DateTimeOffset(now.Year, 12, 31, 0, 0, 0, TimeSpan.Zero),
                CreatedByUserId = SystemUserId
            },
            new Campaign
            {
                OrganisationId = OrganisationId,
                Code = "CMP-2026-HEALTH",
                Name = "Community health outreach",
                Description = "Mobile clinic and medicine appeal.",
                Status = CampaignStatus.Active,
                StartsAtUtc = new DateTimeOffset(now.Year, 6, 1, 0, 0, 0, TimeSpan.Zero),
                CreatedByUserId = SystemUserId
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDonorsAsync(CancellationToken cancellationToken)
    {
        if (await AlreadySeededAsync(context.Donors, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // An active individual with consent, a promise and donation totals: enough for Donor 360
        // to render every panel with real content on a fresh database.
        var individual = new Donor
        {
            OrganisationId = OrganisationId,
            DonorNumber = $"DON-{now.Year:0000}-000001",
            DonorType = DonorType.Individual,
            FirstName = "Arun",
            LastName = "Kumar",
            PrimaryEmail = "arun.kumar@ydot-demo.org",
            PrimaryPhone = "+919876543210",
            PreferredLanguage = "ta-IN",
            Status = DonorStatus.Active,
            ApprovalState = ApprovalState.Approved,
            DoNotContact = false,
            NormalizedBusinessKey = "email:arun.kumar@ydot-demo.org",
            RelationshipOwnerUserId = SystemUserId,
            RelationshipOwnerName = _seed.SystemUserDisplayName,
            ApprovedAtUtc = now.AddDays(-30),
            ApprovedByUserId = SystemUserId,
            SubmittedAtUtc = now.AddDays(-31),
            CreatedByUserId = SystemUserId
        };

        var organisation = new Donor
        {
            OrganisationId = OrganisationId,
            DonorNumber = $"DON-{now.Year:0000}-000002",
            DonorType = DonorType.Organisation,
            OrganisationName = "Meridian Textiles Private Limited",
            PrimaryEmail = "csr@meridian-demo.org",
            PrimaryPhone = "+919812345678",
            PreferredLanguage = "en-IN",
            Status = DonorStatus.Prospect,
            ApprovalState = ApprovalState.PendingApproval,
            NormalizedBusinessKey = "email:csr@meridian-demo.org",
            RelationshipOwnerUserId = SystemUserId,
            RelationshipOwnerName = _seed.SystemUserDisplayName,
            SubmittedAtUtc = now.AddDays(-2),
            CreatedByUserId = SystemUserId
        };

        context.Donors.AddRange(individual, organisation);

        context.DonorContacts.Add(new DonorContact
        {
            Donor = individual,
            Name = "Work e-mail",
            Description = "Reachable on weekdays.",
            Status = DonorContactStatus.Active,
            Channel = ContactChannel.Email,
            Value = "arun.kumar.work@ydot-demo.org",
            IsPrimary = false,
            IsVerified = true,
            VerifiedAtUtc = now.AddDays(-20),
            CreatedByUserId = SystemUserId
        });

        context.DonorTags.Add(new DonorTag
        {
            Donor = individual,
            Code = "MAJOR_GIVER",
            Name = "Major giver",
            Description = "Cumulative giving above the major-gift threshold.",
            Status = DonorTagStatus.Active,
            CreatedByUserId = SystemUserId
        });

        foreach (var channel in new[] { ConsentChannel.Email, ConsentChannel.Sms, ConsentChannel.PhoneCall })
        {
            context.Consents.Add(new Consent
            {
                Donor = individual,
                OrganisationId = OrganisationId,
                Name = $"{channel} consent - {individual.DonorNumber}",
                Status = ConsentStatus.Active,
                Purpose = "Fundraising updates, impact reports and appeal communication.",
                Channel = channel,
                ConsentState = ConsentState.Granted,
                NoticeVersion = _settings.CurrentNoticeVersion,
                EvidenceSource = "Signed paper consent form",
                EvidenceReference = "DOC-CONSENT-000001",
                EffectiveAtUtc = now.AddDays(-30),
                PublicRecognitionPreference = true,
                CapturedByUserId = SystemUserId,
                CapturedByName = _seed.SystemUserDisplayName,
                CreatedByUserId = SystemUserId
            });
        }

        // WhatsApp was offered and declined. Recorded as a real refusal so the follow-up planner
        // has something to block against, rather than as a missing row.
        context.Consents.Add(new Consent
        {
            Donor = individual,
            OrganisationId = OrganisationId,
            Name = $"WhatsApp consent - {individual.DonorNumber}",
            Status = ConsentStatus.Withdrawn,
            Purpose = "Fundraising updates by WhatsApp.",
            Channel = ConsentChannel.WhatsApp,
            ConsentState = ConsentState.Withdrawn,
            NoticeVersion = _settings.CurrentNoticeVersion,
            EvidenceSource = "Telephone call, recorded",
            EffectiveAtUtc = now.AddDays(-15),
            WithdrawnAtUtc = now.AddDays(-15),
            WithdrawalReason = "The donor asked not to be contacted on WhatsApp.",
            CapturedByUserId = SystemUserId,
            CapturedByName = _seed.SystemUserDisplayName,
            CreatedByUserId = SystemUserId
        });

        context.DonorInteractions.Add(new DonorInteraction
        {
            Donor = individual,
            OrganisationId = OrganisationId,
            Name = "Introduction call",
            Description = "Discussed the education appeal. The donor asked for the impact report.",
            Status = DonorInteractionStatus.Completed,
            InteractionType = InteractionType.Call,
            Channel = ConsentChannel.PhoneCall,
            OccurredAtUtc = now.AddDays(-25),
            Outcome = ContactOutcome.Reached,
            PerformedByUserId = SystemUserId,
            PerformedByName = _seed.SystemUserDisplayName,
            CreatedByUserId = SystemUserId
        });

        context.DonorPromises.Add(new DonorPromise
        {
            Donor = individual,
            OrganisationId = OrganisationId,
            Reference = $"PRM-{now.Year:0000}-000001",
            Amount = 50000m,
            Currency = "INR",
            PromisedAtUtc = now.AddDays(-25),
            DueAtUtc = now.AddDays(35),
            Status = PromiseStatus.Open,
            Notes = "Pledged during the introduction call, to be paid after the festival season.",
            CreatedByUserId = SystemUserId
        });

        context.DonorDocuments.Add(new DonorDocument
        {
            Donor = individual,
            OrganisationId = OrganisationId,
            Reference = "DOC-CONSENT-000001",
            Name = "Signed consent form",
            Description = "Scanned paper consent form collected at the education appeal event.",
            Classification = DocumentClassification.Confidential,
            ContentType = "application/pdf",
            SizeInBytes = 184320,
            ScanStatus = "Clean",
            CreatedByUserId = SystemUserId
        });

        context.DonorDonationSummaries.AddRange(
            new DonorDonationSummary
            {
                Donor = individual,
                OrganisationId = OrganisationId,
                Stage = DonationStage.Received,
                Currency = "INR",
                TotalAmount = 125000m,
                TransactionCount = 5,
                AsAtUtc = now.Date,
                RefreshedAtUtc = now,
                CreatedByUserId = SystemUserId
            },
            new DonorDonationSummary
            {
                Donor = individual,
                OrganisationId = OrganisationId,
                Stage = DonationStage.Pledged,
                Currency = "INR",
                TotalAmount = 50000m,
                TransactionCount = 1,
                AsAtUtc = now.Date,
                RefreshedAtUtc = now,
                CreatedByUserId = SystemUserId
            },
            new DonorDonationSummary
            {
                Donor = individual,
                OrganisationId = OrganisationId,
                Stage = DonationStage.Outstanding,
                Currency = "INR",
                TotalAmount = 50000m,
                TransactionCount = 1,
                AsAtUtc = now.Date,
                RefreshedAtUtc = now,
                CreatedByUserId = SystemUserId
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedLeadsAsync(CancellationToken cancellationToken)
    {
        if (await AlreadySeededAsync(context.Leads, cancellationToken))
        {
            return;
        }

        // IgnoreQueryFilters, for exactly the reason set out on AlreadySeededAsync above: this
        // runs at STARTUP, outside any request, so TenantContext.OrganisationId is null and the
        // OrganisationQueryFilter turns every read of Campaigns into `organisation_id = NULL`,
        // which is never true. Without it this lookup always came back null, the method returned
        // at the guard below, and the three demonstration leads were silently never seeded - so
        // the Lead Work Queue opened empty on every fresh database with nothing to explain why.
        //
        // The rows it is looking for were written by SeedCampaignsAsync moments earlier, and the
        // OrganisationId comparison is still made explicitly, so nothing crosses an organisation.
        var campaign = await context.Campaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OrganisationId == OrganisationId && item.Code == "CMP-2026-EDUCATION", cancellationToken);

        if (campaign is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Three leads in three different states, so the work queue shows an unclaimed one, an
        // overdue one and a nurture one the first time somebody opens it.
        context.Leads.AddRange(
            new Lead
            {
                OrganisationId = OrganisationId,
                LeadReference = $"LED-{now.Year:0000}-000001",
                FirstName = "Priya",
                LastName = "Raman",
                MobileNumber = "+919845012345",
                EmailAddress = "priya.raman@ydot-demo.org",
                PreferredLanguage = "ta-IN",
                City = "Coimbatore",
                CampaignId = campaign.Id,
                Source = "Education appeal event",
                ConsentState = ConsentState.Granted,
                Status = LeadStatus.New,
                NextAction = "Introduction call",
                NextActionDueUtc = now.AddDays(2),
                SlaState = SlaState.OnTrack,
                IsDraft = false,
                CreatedByUserId = SystemUserId
            },
            new Lead
            {
                OrganisationId = OrganisationId,
                LeadReference = $"LED-{now.Year:0000}-000002",
                FirstName = "Meera",
                LastName = "Iyer",
                MobileNumber = "+919845098765",
                PreferredLanguage = "en-IN",
                City = "Chennai",
                CampaignId = campaign.Id,
                Source = "Website enquiry form",
                ConsentState = ConsentState.NotProvided,
                Status = LeadStatus.Assigned,
                OwnerUserId = SystemUserId,
                OwnerName = _seed.SystemUserDisplayName,
                TeamCode = "SOUTH",
                NextAction = "Follow up on the enquiry",
                NextActionDueUtc = now.AddDays(-3),
                SlaState = SlaState.Overdue,
                IsDraft = false,
                CreatedByUserId = SystemUserId
            },
            new Lead
            {
                OrganisationId = OrganisationId,
                LeadReference = $"LED-{now.Year:0000}-000003",
                FirstName = "Vikram",
                LastName = "Shetty",
                EmailAddress = "vikram.shetty@ydot-demo.org",
                PreferredLanguage = "kn-IN",
                City = "Bengaluru",
                CampaignId = campaign.Id,
                Source = "Referral from an existing donor",
                ConsentState = ConsentState.Granted,
                Status = LeadStatus.Nurture,
                OwnerUserId = SystemUserId,
                OwnerName = _seed.SystemUserDisplayName,
                TeamCode = "SOUTH",
                LastContactOutcome = ContactOutcome.CallbackRequested,
                LastContactedAtUtc = now.AddDays(-10),
                NextAction = "Call back after the festival season",
                NextActionDueUtc = now.AddDays(20),
                SlaState = SlaState.OnTrack,
                IsDraft = false,
                CreatedByUserId = SystemUserId
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}

