using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Seed;

/// <summary>
/// Demonstration access requests and one recertification campaign, for the activated sample
/// Organisation.
///
/// WHY THESE ARE SEEDED. <see cref="IamDbSeeder"/> creates the Organisation, its roles and its
/// three accounts and stops there, which leaves the two governance screens opening on their own
/// empty states. That is honest but useless: an approval queue with no rows cannot demonstrate
/// the maker-checker rule it exists to enforce, and a recertification screen with no campaign
/// shows a progress bar reading 0 of 0. Both had to be filled by hand before anything about
/// either could be looked at, which is the same gap the sample Organisation itself closes one
/// level up.
///
/// IT IS SEPARATE FROM <see cref="IamDbSeeder"/> RATHER THAN A METHOD ON IT. That seeder builds
/// the platform - permissions, menus, roles, accounts - and everything in it is structure the
/// product cannot run without. This is demonstration content, and it depends on that seeder
/// having finished: the requests name users and roles by their generated ids, which exist only
/// after the accounts are saved.
///
/// IT IS IDEMPOTENT, and by fixed id rather than by counting rows. The seeder runs on every
/// start, so it reads what is already there and inserts only what is missing - and an approver
/// who decides one of these requests, or a reviewer who certifies one of these reviews, keeps
/// their decision on every restart afterwards.
///
/// THE NUMBERS ARE RESERVED FROM THE TOP RATHER THAN FIXED. AR-2026-00001 looks like a safe
/// constant and is not: the number is unique inside the Organisation, and a database where
/// somebody had already raised a request through the product would have that number taken. The
/// highest in use is read and the block continues from it, exactly as
/// <c>NextRequestNumberAsync</c> does for a request raised through the product.
///
/// THE ROWS ARE STAMPED WITH REAL PEOPLE, not with <c>Guid.Empty</c>. Every request names a
/// requester and a subject, every decision names who made it, and the two are always different
/// accounts - because the whole point of the screen is the rule that they must be, and seed data
/// that broke it would demonstrate the opposite of what the module does.
/// </summary>
public sealed class AccessGovernanceSeeder(
    IamDbContext context,
    IOptions<SeedSettings> seedOptions,
    ILogger<AccessGovernanceSeeder> logger)
{
    private readonly SeedSettings _seed = seedOptions.Value;

    /// <summary>
    /// Which of the three seeded accounts a row refers to.
    ///
    /// The accounts' ids are generated when they are created, so nothing here can name one
    /// directly. This is resolved to real ids once, at the top of the seed.
    /// </summary>
    private enum Actor
    {
        /// <summary>The Organisation's own administrator, TENANT_ADMIN.</summary>
        Administrator,

        /// <summary>The maker. Raises things and approves nothing.</summary>
        Initiator,

        /// <summary>The checker.</summary>
        Approver
    }

    /// <summary>
    /// The access requests, one per state the queue can show.
    ///
    /// EVERY STATE IS COVERED ON PURPOSE. The queue's buttons are keyed on status - Submitted has
    /// Approve, Reject and Send back; Draft has Submit and Delete; Returned has Resubmit; and
    /// Approved and Rejected have neither and carry a decision instead. A set that was uniformly
    /// Submitted would leave most of the screen unreachable.
    ///
    /// THE THREE SUBMITTED ROWS ARE DECIDABLE BY DIFFERENT PEOPLE. `CanDecide` is false for the
    /// requester and for the subject, so a queue whose pending rows all belonged to one pair
    /// would show an approver nothing to act on. Each of the three excludes a different account,
    /// which means whichever of the three is signed in has something they may decide and
    /// something they may not - which is the rule made visible rather than merely enforced.
    /// </summary>
    private static readonly (string Id, Actor For, Actor By, string RoleCode,
        AccessRequestType Type, DataScopeType? Scope, string? ScopeValue, string Justification,
        AccessRequestStatus Status, Actor? DecidedBy, string? Notes, bool Sensitive,
        int RaisedDaysAgo, int? EndsInDays)[] Requests =
    [
        // ---- AWAITING A DECISION ----------------------------------------------------------
        //
        // Raised by the administrator for the initiator, so the approver may decide it and the
        // other two may not.
        ("46000000-0000-0000-0000-000000000001", Actor.Initiator, Actor.Administrator,
            RoleCodes.Approver, AccessRequestType.RoleAssignment,
            DataScopeType.Organisation, null,
            "Cover for approvals while the regular approver is on leave from the end of the "
            + "month. Needed for campaign sign-off and payment release only.",
            AccessRequestStatus.Submitted, null, null, false, 3, 45),

        // Raised by the initiator for the administrator: the approver may decide this one too,
        // and it is the temporary-elevation case, which is the one with an end date that means
        // something.
        ("46000000-0000-0000-0000-000000000002", Actor.Administrator, Actor.Initiator,
            RoleCodes.TenantAdmin, AccessRequestType.TemporaryElevation,
            DataScopeType.Campaign, "Hope Foundation Annual Giving",
            "Quarter-end reconciliation needs administrator rights on the annual giving "
            + "campaign for two weeks. Reverts automatically at the end date.",
            AccessRequestStatus.Submitted, null, null, true, 1, 14),

        // Raised by the initiator for the approver, so this is the one the ADMINISTRATOR decides
        // and the approver cannot - the same rule seen from the other side.
        ("46000000-0000-0000-0000-000000000003", Actor.Approver, Actor.Initiator,
            RoleCodes.Initiator, AccessRequestType.DataScopeGrant,
            DataScopeType.Geography, "Tamil Nadu",
            "Taking on the southern region from next month and needs the donor and campaign "
            + "records for Tamil Nadu, which the current whole-organisation scope excludes.",
            AccessRequestStatus.Submitted, null, null, false, 6, null),

        // ---- DECIDED ----------------------------------------------------------------------
        //
        // Approved, with the decision on it. The approver decided it and neither raised it nor
        // is its subject, which is what the handler requires of a real decision.
        ("46000000-0000-0000-0000-000000000004", Actor.Initiator, Actor.Administrator,
            RoleCodes.Initiator, AccessRequestType.RoleAssignment,
            DataScopeType.Organisation, null,
            "Joining the fundraising team and needs the maker role to raise campaigns, donors "
            + "and payment requests.",
            AccessRequestStatus.Approved, Actor.Approver,
            "Approved. The role matches the job and the start date is confirmed.", false, 21, null),

        // Rejected, and the reason says why - which is the difference between a rejection and a
        // return, and the reason the two statuses both exist.
        ("46000000-0000-0000-0000-000000000005", Actor.Initiator, Actor.Administrator,
            RoleCodes.TenantAdmin, AccessRequestType.RoleAssignment,
            DataScopeType.Organisation, null,
            "Asked for administrator rights to manage user accounts during the onboarding push "
            + "next quarter.",
            AccessRequestStatus.Rejected, Actor.Approver,
            "Refused. Administrator rights are not needed for onboarding - raise the accounts "
            + "through the invitation flow, which the maker role already covers.", false, 14, null),

        // ---- SENT BACK --------------------------------------------------------------------
        //
        // Not a decision. The approver could not answer, so it went back for more detail and
        // keeps its number and its history.
        ("46000000-0000-0000-0000-000000000006", Actor.Approver, Actor.Initiator,
            RoleCodes.TenantAdmin, AccessRequestType.PermissionGrant,
            DataScopeType.Organisation, null,
            "Needs to configure the payment gateway.",
            AccessRequestStatus.Returned, Actor.Administrator,
            "Sent back. Say which gateway and for how long - gateway configuration decides where "
            + "the organisation's money settles, and 'needs to configure' is not enough to "
            + "approve on.", true, 9, null),

        // ---- STILL BEING WRITTEN ----------------------------------------------------------
        //
        // A draft the initiator raised for themselves and has not submitted. Drafts are the one
        // case where requester and subject may be the same person: nothing has been asked of
        // anybody yet.
        ("46000000-0000-0000-0000-000000000007", Actor.Initiator, Actor.Initiator,
            RoleCodes.Approver, AccessRequestType.RoleAssignment,
            DataScopeType.Warehouse, "Central store",
            "Stock reconciliation at the central store needs approval rights on goods received.",
            AccessRequestStatus.Draft, null, null, false, 0, 90)
    ];

    /// <summary>
    /// The reviews inside the campaign, one per state the recertification screen can show.
    ///
    /// A REVIEWER IS NEVER THE SUBJECT. Nobody recertifies their own access, which the handler
    /// enforces for sensitive access and which seed data has no excuse to break.
    ///
    /// ONE IS DELIBERATELY OVERDUE, because the campaign's overdue count and the row's own
    /// warning are otherwise unreachable, and "what happens to the ones nobody answered" is the
    /// question a recertification is run to answer.
    /// </summary>
    private static readonly (string Id, Actor Subject, Actor Reviewer, string RoleCode,
        AccessReviewStatus Status, AccessReviewDecision? Decision, string? Reason,
        int DueInDays)[] Reviews =
    [
        // ---- ANSWERED ---------------------------------------------------------------------
        ("47000000-0000-0000-0000-000000000001", Actor.Initiator, Actor.Approver,
            RoleCodes.Initiator, AccessReviewStatus.Completed, AccessReviewDecision.Retain,
            "Still in the fundraising team and still needs the maker role. Retained.", 21),

        ("47000000-0000-0000-0000-000000000002", Actor.Approver, Actor.Administrator,
            RoleCodes.Approver, AccessReviewStatus.Completed, AccessReviewDecision.Retain,
            "Approval rights are the job. Retained.", 21),

        // A revoke, with the reason the decision requires. The screen's whole point is that a
        // recertification can remove access as well as renew it.
        ("47000000-0000-0000-0000-000000000003", Actor.Administrator, Actor.Approver,
            RoleCodes.TenantAdmin, AccessReviewStatus.Completed, AccessReviewDecision.Modify,
            "Keeps administrator rights for user management, but the payment gateway "
            + "permissions are no longer part of the role and have been removed.", 21),

        // ---- STILL OPEN -------------------------------------------------------------------
        ("47000000-0000-0000-0000-000000000004", Actor.Initiator, Actor.Administrator,
            RoleCodes.Approver, AccessReviewStatus.Open, null, null, 21),

        ("47000000-0000-0000-0000-000000000005", Actor.Approver, Actor.Initiator,
            RoleCodes.Initiator, AccessReviewStatus.InProgress, null, null, 21),

        // ---- OVERDUE ----------------------------------------------------------------------
        //
        // Due a week ago and still unanswered. With `RevokeOnNoResponse` set on the campaign,
        // this is the row that gets revoked when the campaign closes - which is the behaviour
        // the flag exists for and cannot be demonstrated without a row in this state.
        ("47000000-0000-0000-0000-000000000006", Actor.Administrator, Actor.Initiator,
            RoleCodes.Initiator, AccessReviewStatus.Open, null, null, -7)
    ];

    /// <summary>The campaign the reviews belong to. Fixed, so the seed recognises its own row.</summary>
    private static readonly Guid CampaignId = Guid.Parse("48000000-0000-0000-0000-000000000001");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_seed.Enabled || !_seed.SeedSampleTenants)
        {
            return;
        }

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == _seed.SampleOrganisationId)
            .Select(item => new { item.Id, item.Code, item.BusinessUnitId })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
        {
            // IamDbSeeder creates it and runs first. Reaching here means sample Organisations
            // are switched off, or the id was overridden without the seeder being re-run.
            logger.LogInformation(
                "The sample Organisation does not exist, so the governance demonstration data "
                + "was skipped.");

            return;
        }

        var actors = await ResolveActorsAsync(tenant.Id, cancellationToken);

        if (actors is null)
        {
            // The role accounts need SeedSettings:RoleAccountPassword, which a deployment may
            // legitimately leave unset. Without them there is nobody to raise a request FOR, and
            // stamping the rows with Guid.Empty would produce a queue whose every row shows a
            // blank name - the list projection inner-joins the subject.
            logger.LogInformation(
                "The demonstration accounts are not all present in {Tenant}, so the governance "
                + "demonstration data was skipped.", tenant.Code);

            return;
        }

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(role => role.TenantId == tenant.Id)
            .ToDictionaryAsync(role => role.Code, role => role.Id, StringComparer.Ordinal, cancellationToken);

        await SeedRequestsAsync(tenant.Id, tenant.BusinessUnitId, actors, roles, cancellationToken);
        await SeedCampaignAsync(tenant.Id, tenant.BusinessUnitId, actors, roles, cancellationToken);
    }

    // =============================================================================================
    // Access requests
    // =============================================================================================

    private async Task SeedRequestsAsync(
        Guid tenantId, Guid businessUnitId, IReadOnlyDictionary<Actor, Guid> actors,
        IReadOnlyDictionary<string, Guid> roles, CancellationToken cancellationToken)
    {
        var present = (await context.AccessRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(request => request.TenantId == tenantId)
                .Select(request => request.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = Requests.Where(seed => !present.Contains(Guid.Parse(seed.Id))).ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var numbers = await ReserveNumbersAsync(
            context.AccessRequests
                .IgnoreQueryFilters()
                .Where(request => request.TenantId == tenantId)
                .Select(request => request.RequestNumber),
            "AR", missing.Count, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        foreach (var (seed, index) in missing.Select((value, index) => (value, index)))
        {
            var raisedAt = now.AddDays(-seed.RaisedDaysAgo);
            var isDraft = seed.Status == AccessRequestStatus.Draft;

            var request = new AccessRequest
            {
                Id = Guid.Parse(seed.Id),
                TenantId = tenantId,
                BusinessUnitId = businessUnitId,
                RequestNumber = numbers[index],
                RequestedForUserId = actors[seed.For],
                RequestedByUserId = actors[seed.By],
                RequestType = seed.Type,
                RoleId = roles.TryGetValue(seed.RoleCode, out var roleId) ? roleId : null,
                ScopeType = seed.Scope,
                ScopeValue = seed.ScopeValue,
                BusinessJustification = seed.Justification,
                Status = seed.Status,
                IsSensitive = seed.Sensitive,

                // The access starts when it is granted, so an undecided request asks for it from
                // now. A decided one already has its start behind it.
                AccessStartsAtUtc = raisedAt,
                AccessEndsAtUtc = seed.EndsInDays.HasValue
                    ? raisedAt.AddDays(seed.EndsInDays.Value)
                    : null,

                // NOT SET ON A DRAFT. Nothing has been submitted, so a submission time would be
                // a date for an event that has not happened.
                SubmittedAtUtc = isDraft ? null : raisedAt,

                CreatedAtUtc = raisedAt,
                CreatedByUserId = actors[seed.By],
                Version = 1
            };

            // A SUBMITTED REQUEST LAPSES IF NOBODY ANSWERS IT, which is what stops the queue
            // filling with rows nobody will ever decide. Thirty days from submission, and far
            // enough out that the seeded rows are still pending when somebody looks at them.
            if (seed.Status == AccessRequestStatus.Submitted)
            {
                request.ExpiresAtUtc = raisedAt.AddDays(30);
            }

            switch (seed.Status)
            {
                case AccessRequestStatus.Approved or AccessRequestStatus.Rejected:
                    request.DecidedByUserId = actors[seed.DecidedBy!.Value];
                    request.DecidedAtUtc = raisedAt.AddDays(1);
                    request.DecisionNotes = seed.Notes;
                    break;

                // A RETURN IS NOT A DECISION, so it fills the return columns and leaves the
                // decision ones empty. Collapsing the two would make "sent back for more detail"
                // read in the audit trail as "refused".
                case AccessRequestStatus.Returned:
                    request.ReturnedByUserId = actors[seed.DecidedBy!.Value];
                    request.ReturnedAtUtc = raisedAt.AddDays(1);
                    request.ReturnReason = seed.Notes;
                    request.ReturnCount = 1;
                    break;

                default:
                    break;
            }

            await context.AccessRequests.AddAsync(request, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Count} demonstration access request(s).", missing.Count);
    }

    // =============================================================================================
    // The recertification campaign
    // =============================================================================================

    private async Task SeedCampaignAsync(
        Guid tenantId, Guid businessUnitId, IReadOnlyDictionary<Actor, Guid> actors,
        IReadOnlyDictionary<string, Guid> roles, CancellationToken cancellationToken)
    {
        // TRACKED, NOT AsNoTracking. The counts at the bottom are written onto this instance,
        // so it has to be the one the change tracker will save. Null means it does not exist yet
        // and is created below.
        var campaign = await context.AccessReviewCampaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == CampaignId, cancellationToken);

        var present = (await context.AccessReviews
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(review => review.TenantId == tenantId)
                .Select(review => review.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = Reviews.Where(seed => !present.Contains(Guid.Parse(seed.Id))).ToList();

        if (campaign is not null && missing.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // ONE BLOCK FOR THE WHOLE BATCH, reserved before the loop. `ReviewNumber` is unique
        // inside the Organisation, so leaving it at its default would give every seeded review
        // the empty string and the index would refuse the second one - the same failure the
        // repository's `NextReviewNumbersAsync` exists to avoid when a campaign is raised
        // through the product.
        var reviewNumbers = await ReserveNumbersAsync(
            context.AccessReviews
                .IgnoreQueryFilters()
                .Where(review => review.TenantId == tenantId)
                .Select(review => review.ReviewNumber),
            "REV", missing.Count, cancellationToken);

        if (campaign is null)
        {
            // THE CODE CARRIES THE CURRENT YEAR AND QUARTER, not a written-in one. A campaign
            // coded REV-2026-Q3 on a database created in 2027 reads as a stale record somebody
            // forgot to close, which is the opposite of what a demonstration row should say.
            //
            // AND IT WALKS PAST A CODE ALREADY TAKEN. The code is unique inside the Organisation,
            // so an administrator who has already raised this quarter's recertification by hand
            // owns the obvious name - and a collision on the index would fail the whole start-up
            // rather than skip one demonstration row.
            var quarter = ((now.Month - 1) / 3) + 1;

            var code = await NextFreeCampaignCodeAsync(
                tenantId,
                string.Create(CultureInfo.InvariantCulture, $"REV-{now.Year}-Q{quarter}"),
                cancellationToken);

            campaign = new AccessReviewCampaign
            {
                Id = CampaignId,
                TenantId = tenantId,
                BusinessUnitId = businessUnitId,
                Code = code,
                Name = "Quarterly access recertification",
                Description =
                    "Every role assignment in the organisation, confirmed or removed by the "
                    + "person responsible for it.",

                // ACTIVE AND RUNNING, because a draft campaign has issued no reviews and a
                // closed one has finished with them - neither is a state the screen is worth
                // opening in. It started a fortnight ago and is due in three weeks.
                Status = AccessReviewCampaignStatus.Active,
                StartsAtUtc = now.AddDays(-14),
                DueAtUtc = now.AddDays(21),

                // FAIL CLOSED. Silence should not renew access, and it is what makes the one
                // overdue review below mean something.
                RevokeOnNoResponse = true,

                CreatedAtUtc = now.AddDays(-14),
                CreatedByUserId = actors[Actor.Administrator],
                Version = 1
            };

            await context.AccessReviewCampaigns.AddAsync(campaign, cancellationToken);
        }

        foreach (var (seed, index) in missing.Select((value, index) => (value, index)))
        {
            var review = new AccessReview
            {
                Id = Guid.Parse(seed.Id),
                ReviewNumber = reviewNumbers[index],
                TenantId = tenantId,
                BusinessUnitId = businessUnitId,
                CampaignId = CampaignId,
                SubjectUserId = actors[seed.Subject],
                ReviewerUserId = actors[seed.Reviewer],
                RoleId = roles.TryGetValue(seed.RoleCode, out var roleId) ? roleId : null,

                // WHAT WAS HELD WHEN THE REVIEW WAS RAISED. Snapshotted rather than read live,
                // so a change made since cannot quietly alter what the reviewer was asked about.
                AccessSnapshot = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Role {seed.RoleCode}, whole organisation, held since the account was created."),

                ReviewDueAtUtc = now.AddDays(seed.DueInDays),
                Status = seed.Status,
                Decision = seed.Decision,
                DecisionReason = seed.Reason,

                CreatedAtUtc = now.AddDays(-14),
                CreatedByUserId = actors[Actor.Administrator],
                Version = 1
            };

            if (seed.Status == AccessReviewStatus.Completed)
            {
                review.StartedAtUtc = now.AddDays(-10);
                review.DecidedAtUtc = now.AddDays(-9);
                review.CompletedAtUtc = now.AddDays(-9);

                // RETAIN NEEDS NO ACTION, so it is applied the moment it is decided. Modify and
                // Revoke change something, and the flag says whether that has happened yet.
                review.IsDecisionApplied = true;
                review.DecisionAppliedAtUtc = now.AddDays(-9);
            }
            else if (seed.Status == AccessReviewStatus.InProgress)
            {
                review.StartedAtUtc = now.AddDays(-2);
            }

            await context.AccessReviews.AddAsync(review, cancellationToken);
        }

        // The counts the progress bar reads. Snapshots on the campaign rather than an aggregate
        // per render, so they are written here in the same shape closing the campaign would
        // leave them - counted over every review in the campaign, not only the ones just added.
        //
        // WRITTEN ONTO THE INSTANCE ABOVE rather than onto a fresh read. On a first run the row
        // has been added but not saved, so re-reading it from the database would find nothing.
        campaign.TotalReviewCount = Reviews.Length;
        campaign.CompletedReviewCount =
            Reviews.Count(review => review.Status == AccessReviewStatus.Completed);
        campaign.OverdueReviewCount = Reviews.Count(review =>
            review.DueInDays < 0
            && review.Status is AccessReviewStatus.Open or AccessReviewStatus.InProgress);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded the demonstration recertification campaign and {Count} review(s).",
            missing.Count);
    }

    // =============================================================================================
    // Shared
    // =============================================================================================

    /// <summary>
    /// The three seeded accounts, by the part they play.
    ///
    /// THE ADMINISTRATOR IS FOUND BY ITS FLAG rather than by its address. The sample
    /// Organisation's e-mail is configuration and a deployment may change it; `IsTenantAdmin` is
    /// what the account actually is.
    ///
    /// NULL WHEN ANY OF THE THREE IS MISSING, and the whole seed is skipped rather than a
    /// partial one written. Every row here names two different people, so a set with a gap in it
    /// would have to either stamp <c>Guid.Empty</c> - producing rows the list projection drops,
    /// because it inner-joins the subject - or quietly pair somebody with themselves, which is
    /// the one thing this data exists to show cannot happen.
    /// </summary>
    private async Task<IReadOnlyDictionary<Actor, Guid>?> ResolveActorsAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var users = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .Select(user => new { user.Id, user.UserName, user.IsTenantAdmin })
            .ToListAsync(cancellationToken);

        var administrator = users.FirstOrDefault(user => user.IsTenantAdmin);

        var initiator = users.FirstOrDefault(user =>
            string.Equals(user.UserName, "initiator", StringComparison.OrdinalIgnoreCase));

        var approver = users.FirstOrDefault(user =>
            string.Equals(user.UserName, "approver", StringComparison.OrdinalIgnoreCase));

        if (administrator is null || initiator is null || approver is null)
        {
            return null;
        }

        return new Dictionary<Actor, Guid>
        {
            [Actor.Administrator] = administrator.Id,
            [Actor.Initiator] = initiator.Id,
            [Actor.Approver] = approver.Id
        };
    }

    /// <summary>
    /// The preferred campaign code, or the first free variant of it.
    ///
    /// Suffixed rather than abandoned, so the seeded campaign still names the quarter it covers
    /// when the plain code is taken. Ten attempts is far past any realistic case; beyond that the
    /// caller gets the preferred code back and the insert fails loudly, which is better than
    /// looping.
    /// </summary>
    private async Task<string> NextFreeCampaignCodeAsync(
        Guid tenantId, string preferred, CancellationToken cancellationToken)
    {
        var taken = await context.AccessReviewCampaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(campaign => campaign.TenantId == tenantId && campaign.Code.StartsWith(preferred))
            .Select(campaign => campaign.Code)
            .ToListAsync(cancellationToken);

        if (!taken.Contains(preferred, StringComparer.OrdinalIgnoreCase))
        {
            return preferred;
        }

        for (var attempt = 2; attempt <= 10; attempt++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{preferred}-{attempt}");

            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return preferred;
    }

    /// <summary>
    /// A block of reference numbers continuing from the highest already in use this year.
    ///
    /// THE SAME ARITHMETIC AS <c>NextNumbersAsync</c> IN THE REPOSITORY, and duplicated here on
    /// purpose rather than reached through it: that method is on a repository whose other members
    /// this seeder has no business holding, and the alternative - fixed numbers written into the
    /// blueprint - collides on any database where somebody has already raised a request through
    /// the product. A collision on a unique index would fail the whole start-up.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReserveNumbersAsync(
        IQueryable<string> existingNumbers, string prefix, int count,
        CancellationToken cancellationToken)
    {
        var stem = string.Create(
            CultureInfo.InvariantCulture, $"{prefix}-{DateTimeOffset.UtcNow.Year}-");

        var highest = await existingNumbers
            .Where(number => number.StartsWith(stem))
            .OrderByDescending(number => number)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 0;

        if (highest is not null
            && int.TryParse(
                highest[stem.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            next = parsed;
        }

        return
        [
            .. Enumerable.Range(next + 1, count)
                .Select(value => string.Create(CultureInfo.InvariantCulture, $"{stem}{value:D5}"))
        ];
    }
}
