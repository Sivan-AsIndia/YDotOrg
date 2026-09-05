using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Governance.DTOs;
using YDot.IAM.Application.Features.Governance.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Application.Features.Governance.Commands.AccessReviews;

/// <summary>Raises a batch of reviews.</summary>
public sealed record CreateAccessReviewCampaignCommand(CreateAccessReviewCampaignRequest Request);

/// <summary>Raises a single review outside any campaign.</summary>
public sealed record CreateAccessReviewCommand(CreateAccessReviewRequest Request);

/// <summary>Records a decision.</summary>
public sealed record DecideAccessReviewCommand(Guid Id, DecideAccessReviewRequest Request);

/// <summary>Cancels a review.</summary>
public sealed record CancelAccessReviewCommand(Guid Id, CancelAccessReviewRequest Request);

/// <summary>Hands a review to somebody better placed to answer it.</summary>
public sealed record DelegateAccessReviewCommand(Guid Id, DelegateAccessReviewRequest Request);

/// <summary>Escalates a review the reviewer cannot answer alone.</summary>
public sealed record EscalateAccessReviewCommand(Guid Id, EscalateAccessReviewRequest Request);

/// <summary>Closes a campaign and applies every outstanding decision.</summary>
public sealed record CloseAccessReviewCampaignCommand(Guid Id, CloseAccessReviewCampaignRequest Request);

/// <summary>
/// Access reviews: periodic recertification of who holds what.
///
/// THE SNAPSHOT IS THE INTERESTING PART. When a review is raised, what the person currently
/// holds is captured onto the row. The reviewer then decides against THAT, not against a live
/// read — so a change made in between cannot quietly alter what they were asked about, and
/// the record shows exactly what was recertified.
///
/// FAILING CLOSED. A campaign can be set to treat anything still open at the due date as
/// Revoke rather than Retain. That is the right default for a recertification: silence should
/// not renew access, and the whole point of the exercise is to remove what nobody will vouch
/// for.
/// </summary>
public sealed class AccessReviewCommandHandler(
    IGovernanceRepository governance,
    IUserRepository users,
    IRoleRepository roles,
    ITenantRepository tenants,
    IBusinessUnitRepository businessUnits,
    INotificationService notifications,
    IAuditService audit,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<ClientAppSettings> clientApp,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<AccessReviewCampaignResponse>> HandleAsync(
        CreateAccessReviewCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<AccessReviewCampaignResponse>(Error.TenantSelectionRequired());
        }

        var tenantId = tenantContext.RequireTenantId();

        if (request.DueAtUtc <= now)
        {
            return Result.Failure<AccessReviewCampaignResponse>(
                Error.Validation("The due date must be in the future.",
                    [new ValidationError(nameof(request.DueAtUtc), "Choose a date in the future.")]));
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? CodeValue.FromName(request.Name)
            : CodeValue.TryParse(request.Code)?.Value ?? CodeValue.FromName(request.Name);

        if (await governance.CampaignCodeExistsAsync(code, tenantId, null, cancellationToken))
        {
            return Result.Failure<AccessReviewCampaignResponse>(
                Error.Duplicate($"A campaign with code {code} already exists."));
        }

        var campaign = new AccessReviewCampaign
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = AccessReviewCampaignStatus.Active,
            StartsAtUtc = request.StartsAtUtc ?? now,
            DueAtUtc = request.DueAtUtc,
            RevokeOnNoResponse = request.RevokeOnNoResponse
        };

        await governance.AddCampaignAsync(campaign, cancellationToken);

        // ---- Work out what to review ---------------------------------------------------
        var candidates = await BuildCandidatesAsync(request, tenantId, now, cancellationToken);

        // ONE RESERVATION FOR THE WHOLE CAMPAIGN, taken before the loop.
        //
        // This used to call NextReviewNumberAsync per review. Nothing is saved until the end of
        // the handler, so every one of those calls counted the same unchanged table and returned
        // REV-yyyy-00001 - and the unique index on the number refused the batch. Creating a
        // campaign that covered two or more people failed outright with a 500, which is to say
        // the screen's whole purpose did not work.
        //
        // Sized to the candidate list rather than to the reviews actually raised: some
        // candidates are skipped below when no reviewer can be found, and a gap in a reference
        // series costs nothing while running out of numbers mid-loop would cost everything.
        var numbers = await governance.NextReviewNumbersAsync(
            tenantId, candidates.Count, cancellationToken);

        var created = 0;

        foreach (var candidate in candidates)
        {
            // THE REVIEWER MUST NOT BE THE SUBJECT. Nobody recertifies their own access, so a
            // person with no manager falls to the Organisation administrator rather than to
            // themselves.
            var reviewerId = candidate.ManagerUserId
                             ?? await ResolveFallbackReviewerAsync(tenantId, candidate.UserId, cancellationToken);

            if (reviewerId is null || reviewerId == candidate.UserId)
            {
                continue;
            }

            await governance.AddAccessReviewAsync(new AccessReview
            {
                TenantId = tenantId,
                BusinessUnitId = tenantContext.BusinessUnitId,
                ReviewNumber = numbers[created],
                CampaignId = campaign.Id,
                SubjectUserId = candidate.UserId,
                ReviewerUserId = reviewerId.Value,
                UserRoleId = candidate.UserRoleId,
                RoleId = candidate.RoleId,
                // Captured now, so a later change cannot alter what was asked about.
                AccessSnapshot = candidate.Snapshot,
                ReviewDueAtUtc = request.DueAtUtc,
                Status = AccessReviewStatus.Open
            }, cancellationToken);

            created++;
        }

        campaign.TotalReviewCount = created;

        await audit.WriteAsync(
            AuditActionCodes.AccessReviewCreated, nameof(AccessReviewCampaign), campaign.Id, campaign.Name,
            new { campaign.Code, ReviewCount = created, campaign.RevokeOnNoResponse },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AccessReviewCampaignResponse(
            campaign.Id, campaign.Code, campaign.Name, campaign.Description,
            campaign.Status, GovernanceMappingConfig.Humanise(campaign.Status.ToString()),
            campaign.StartsAtUtc, campaign.DueAtUtc, campaign.ClosedAtUtc, null,
            campaign.TotalReviewCount, campaign.CompletedReviewCount, campaign.OverdueReviewCount,
            campaign.PercentComplete, campaign.RevokeOnNoResponse, campaign.CreatedAtUtc,
            campaign.Version, GovernanceMappingConfig.PermittedActionsFor(campaign)));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        CreateAccessReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (!tenantContext.HasTenant)
        {
            return Result.Failure<OutcomeResponse>(Error.TenantSelectionRequired());
        }

        if (request.SubjectUserId == request.ReviewerUserId)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "Somebody cannot review their own access."));
        }

        var tenantId = tenantContext.RequireTenantId();

        var subject = await users.GetWithAccessAsync(request.SubjectUserId, cancellationToken);
        if (subject is null)
        {
            return Result.Failure<OutcomeResponse>(Error.UserNotFound());
        }

        var reviewer = await users.GetByIdAsync(request.ReviewerUserId, cancellationToken);
        if (reviewer is null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That reviewer was not found in this organisation."));
        }

        var review = new AccessReview
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            ReviewNumber = await governance.NextReviewNumberAsync(tenantId, cancellationToken),
            SubjectUserId = subject.Id,
            ReviewerUserId = reviewer.Id,
            UserRoleId = request.UserRoleId,
            RoleId = request.RoleId,
            AccessSnapshot = BuildSnapshot(subject),
            ReviewDueAtUtc = request.ReviewDueAtUtc,
            Status = AccessReviewStatus.Open
        };

        await governance.AddAccessReviewAsync(review, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.AccessReviewCreated, nameof(AccessReview), review.Id, review.ReviewNumber,
            new { SubjectId = subject.Id, ReviewerId = reviewer.Id },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyReviewerAsync(review, reviewer, cancellationToken);

        return Result.Success(new OutcomeResponse(
            review.Id, review.Status.ToString(), review.Version,
            $"Review {review.ReviewNumber} raised.",
            GovernanceMappingConfig.PermittedActionsFor(review, currentUser.UserId)));
    }

    /// <summary>
    /// Records a decision, and carries it out.
    ///
    /// Only the assigned reviewer may decide. A Revoke or Modify without a reason is refused,
    /// because the person losing access deserves an explanation and the auditor needs one.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DecideAccessReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var now = clock.UtcNow;

        var review = await governance.GetAccessReviewAsync(command.Id, cancellationToken);
        if (review is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That review was not found."));
        }

        if (review.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!review.IsOpen && review.Status != AccessReviewStatus.Overdue)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A review that is {review.Status} cannot be decided."));
        }

        if (review.ReviewerUserId != currentUser.UserId && !currentUser.IsSuperAdmin)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Forbidden("Only the assigned reviewer can decide this review."));
        }

        if (review.Decision != AccessReviewDecision.Retain
            && request.Decision != AccessReviewDecision.Retain
            && string.IsNullOrWhiteSpace(request.DecisionReason))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Give a reason for changing or removing this access.",
                    [new ValidationError(nameof(request.DecisionReason),
                        "A reason is required for Modify and Revoke.")]));
        }

        review.Decision = request.Decision;
        review.DecisionReason = request.DecisionReason;
        review.DecidedAtUtc = now;
        review.CompletedAtUtc = now;
        review.Status = AccessReviewStatus.Completed;

        if (request.ApplyImmediately && request.Decision == AccessReviewDecision.Revoke)
        {
            await ApplyRevocationAsync(review, now, cancellationToken);
        }

        // Keep the campaign progress counts current, so the dashboard needs no aggregate query.
        if (review.CampaignId.HasValue)
        {
            var campaign = await governance.GetCampaignAsync(review.CampaignId.Value, cancellationToken);
            if (campaign is not null)
            {
                campaign.CompletedReviewCount += 1;
            }
        }

        await audit.WriteAsync(
            AuditActionCodes.AccessReviewDecided, nameof(AccessReview), review.Id, review.ReviewNumber,
            new { request.Decision, request.DecisionReason, request.ApplyImmediately },
            request.DecisionReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            review.Id, review.Status.ToString(), review.Version,
            request.Decision == AccessReviewDecision.Revoke && request.ApplyImmediately
                ? "Decision recorded and the access removed."
                : "Decision recorded.",
            GovernanceMappingConfig.PermittedActionsFor(review, currentUser.UserId)));
    }

    /// <summary>
    /// Hands a review to somebody better placed to answer it.
    ///
    /// The ORIGINAL reviewer is kept, because "who was asked" and "who answered" are different
    /// questions and an audit of a certification wants both. A review delegated three times
    /// before anybody decided is a different story from one answered by the person it was given
    /// to, and only the original assignment makes that visible.
    ///
    /// It cannot be handed to the SUBJECT of the review. That would be somebody certifying their
    /// own access by the back door, which is the one rule this whole module exists to enforce.
    /// </summary>
    public Task<Result<OutcomeResponse>> HandleAsync(
        DelegateAccessReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return HandOverAsync(
            command.Id,
            command.Request.ReviewerUserId,
            command.Request.Reason,
            command.Request.ExpectedVersion,
            escalated: false,
            cancellationToken);
    }

    /// <summary>
    /// Escalates a review the reviewer cannot answer alone.
    ///
    /// Mechanically the same handover as a delegation, recorded differently on purpose. A
    /// delegation says "you are better placed to answer this". An escalation says "this access
    /// looks wrong and removing it is above my authority" — which is precisely the signal a
    /// governance report needs to be able to count, and which would vanish if both were stored
    /// as the same event.
    /// </summary>
    public Task<Result<OutcomeResponse>> HandleAsync(
        EscalateAccessReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return HandOverAsync(
            command.Id,
            command.Request.EscalateToUserId,
            command.Request.Reason,
            command.Request.ExpectedVersion,
            escalated: true,
            cancellationToken);
    }

    /// <summary>
    /// The handover both of the above perform, with one flag deciding what it is called.
    ///
    /// One implementation rather than two, because every rule is shared: the review must be open,
    /// the version must match, the new reviewer must exist in this Organisation, and they must
    /// not be the subject. Two copies would eventually disagree about one of the four, and the
    /// one that forgot the subject check is the one that lets somebody certify themselves.
    /// </summary>
    private async Task<Result<OutcomeResponse>> HandOverAsync(
        Guid reviewId,
        Guid newReviewerId,
        string reason,
        long expectedVersion,
        bool escalated,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var review = await governance.GetAccessReviewAsync(reviewId, cancellationToken);
        if (review is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That review was not found."));
        }

        if (review.Version != expectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (!review.IsOpen)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That review has already been decided."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("Say why you are handing this on.",
                    [new ValidationError(nameof(reason), "A reason is required.")]));
        }

        // Not found means not in THIS Organisation - the query filter has already excluded
        // everybody else's people, so there is nothing further to check.
        var newReviewer = await users.GetByIdAsync(newReviewerId, cancellationToken);
        if (newReviewer is null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That person was not found in this organisation."));
        }

        // The rule this module exists for: nobody certifies their own access, including by being
        // handed the review for it.
        if (newReviewerId == review.SubjectUserId)
        {
            return Result.Failure<OutcomeResponse>(Error.Forbidden(
                "A review cannot be given to the person whose access is being reviewed."));
        }

        if (newReviewerId == review.ReviewerUserId)
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "That review is already assigned to them.",
                [new ValidationError("ReviewerUserId", "Choose a different person.")]));
        }

        review.OriginalReviewerUserId ??= review.ReviewerUserId;
        review.DelegatedByUserId = currentUser.UserId;
        review.DelegatedAtUtc = now;
        review.DelegationReason = reason.Trim();
        review.WasEscalated = escalated;
        review.ReviewerUserId = newReviewerId;

        await audit.WriteAsync(
            escalated ? AuditActionCodes.AccessReviewEscalated : AuditActionCodes.AccessReviewDelegated,
            nameof(AccessReview), review.Id, review.ReviewNumber,
            new
            {
                From = review.OriginalReviewerUserId,
                To = newReviewerId,
                Reason = reason,
            },
            reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            review.Id, review.Status.ToString(), review.Version,
            escalated
                ? $"Escalated to {newReviewer.DisplayName}."
                : $"Handed to {newReviewer.DisplayName}.",
            ["View"]));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        CancelAccessReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var review = await governance.GetAccessReviewAsync(command.Id, cancellationToken);
        if (review is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That review was not found."));
        }

        if (review.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (review.Status == AccessReviewStatus.Completed)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "A completed review cannot be cancelled."));
        }

        review.Status = AccessReviewStatus.Cancelled;
        review.CancelledAtUtc = clock.UtcNow;
        review.CancellationReason = command.Request.Reason;

        await audit.WriteAsync(
            AuditActionCodes.AccessReviewCancelled, nameof(AccessReview), review.Id,
            review.ReviewNumber, new { command.Request.Reason },
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            review.Id, review.Status.ToString(), review.Version, "Review cancelled.", ["View"]));
    }

    /// <summary>
    /// Closes a campaign.
    ///
    /// THE FAIL-CLOSED RULE FIRES HERE. If the campaign was configured to revoke on no
    /// response, every review still open is decided as Revoke and applied — because a
    /// recertification nobody answered has told you nothing, and leaving the access in place
    /// would make the whole exercise theatre.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        CloseAccessReviewCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.UtcNow;

        var campaign = await governance.GetCampaignAsync(command.Id, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That campaign was not found."));
        }

        if (campaign.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (campaign.Status != AccessReviewCampaignStatus.Active)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"A campaign that is {campaign.Status} cannot be closed."));
        }

        var reviews = await governance.GetReviewsForCampaignAsync(campaign.Id, cancellationToken);
        var outstanding = reviews.Where(review => review.IsOpen || review.Status == AccessReviewStatus.Overdue)
            .ToList();

        var revoked = 0;

        foreach (var review in outstanding)
        {
            if (campaign.RevokeOnNoResponse)
            {
                review.Decision = AccessReviewDecision.Revoke;
                review.DecisionReason = "No response by the campaign due date.";
                await ApplyRevocationAsync(review, now, cancellationToken);
                revoked++;
            }
            else
            {
                review.Decision = AccessReviewDecision.Retain;
                review.DecisionReason = "No response by the campaign due date; access retained.";
            }

            review.DecidedAtUtc = now;
            review.CompletedAtUtc = now;
            review.Status = AccessReviewStatus.Completed;
        }

        campaign.Status = AccessReviewCampaignStatus.Closed;
        campaign.ClosedAtUtc = now;
        campaign.ClosedByUserId = currentUser.UserId;
        campaign.CompletedReviewCount = reviews.Count;
        campaign.OverdueReviewCount = outstanding.Count;

        await audit.WriteAsync(
            AuditActionCodes.AccessReviewDecided, nameof(AccessReviewCampaign), campaign.Id,
            campaign.Name,
            new
            {
                Closed = true,
                OutstandingAtClose = outstanding.Count,
                RevokedOnNoResponse = revoked,
                command.Request.Notes
            },
            command.Request.Notes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            campaign.Id, campaign.Status.ToString(), campaign.Version,
            revoked > 0
                ? $"Campaign closed. {revoked} unanswered review(s) were revoked."
                : $"Campaign closed. {outstanding.Count} unanswered review(s) were retained.",
            ["View"]));
    }

    /// <summary>Carries out a Revoke decision: ends the assignment and invalidates the token.</summary>
    private async Task ApplyRevocationAsync(
        AccessReview review, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (review.UserRoleId.HasValue || review.RoleId.HasValue)
        {
            var assignments = await roles.GetUserRolesAsync(review.SubjectUserId, cancellationToken);

            var target = review.UserRoleId.HasValue
                ? assignments.FirstOrDefault(item => item.Id == review.UserRoleId.Value)
                : assignments.FirstOrDefault(item =>
                    item.RoleId == review.RoleId!.Value
                    && item.Status == UserRoleAssignmentStatus.Active);

            if (target is not null && target.Status == UserRoleAssignmentStatus.Active)
            {
                target.Status = UserRoleAssignmentStatus.Revoked;
                target.RevokedAtUtc = now;
                target.RevokedByUserId = currentUser.UserId;
                target.RevocationReason = review.DecisionReason ?? "Removed by access review.";
            }
        }

        // The subject tokens must stop being trusted, or the removed access would persist
        // until the next refresh.
        var subject = await users.GetByIdAsync(review.SubjectUserId, cancellationToken);
        if (subject is not null)
        {
            subject.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        review.IsDecisionApplied = true;
        review.DecisionAppliedAtUtc = now;
    }

    /// <summary>Everything the campaign should review, as a flat candidate list.</summary>
    private async Task<IReadOnlyList<ReviewCandidate>> BuildCandidatesAsync(
        CreateAccessReviewCampaignRequest request, Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ReviewCandidate>();

        var userIds = request.UserIds is { Count: > 0 }
            ? request.UserIds
            : null;

        var assignableRoles = await roles.GetAssignableAsync(tenantId, cancellationToken);

        var roleFilter = request.RoleIds is { Count: > 0 }
            ? request.RoleIds.ToHashSet()
            : null;

        var subjects = userIds is null
            ? []
            : await users.GetManyAsync(userIds, cancellationToken);

        // No explicit user list means every role holder, which is what a full recertification
        // is. Walked per role rather than per user so the role filter applies cleanly.
        if (userIds is null)
        {
            foreach (var role in assignableRoles.Where(role => roleFilter is null || roleFilter.Contains(role.Id)))
            {
                if (request.SensitiveOnly && !role.IsPrivileged && !role.GrantsAllTenantPermissions)
                {
                    var rolePermissions = await roles.GetRolePermissionsAsync(role.Id, cancellationToken);

                    if (!rolePermissions.Any(grant => PermissionCodes.IsSensitive(grant.PermissionCode)))
                    {
                        continue;
                    }
                }

                var members = await roles.GetRoleMembersAsync(role.Id, cancellationToken);

                foreach (var member in members.Where(item => item.IsEffective(now)))
                {
                    candidates.Add(new ReviewCandidate(
                        member.UserId,
                        member.User?.ManagerUserId,
                        member.Id,
                        role.Id,
                        $"{role.Code} ({role.Name ?? role.Code})"));
                }
            }

            return candidates;
        }

        foreach (var subject in subjects)
        {
            var assignments = await roles.GetUserRolesAsync(subject.Id, cancellationToken);

            foreach (var assignment in assignments.Where(item => item.IsEffective(now)))
            {
                if (roleFilter is not null && !roleFilter.Contains(assignment.RoleId))
                {
                    continue;
                }

                candidates.Add(new ReviewCandidate(
                    subject.Id,
                    subject.ManagerUserId,
                    assignment.Id,
                    assignment.RoleId,
                    $"{assignment.Role?.Code} ({assignment.Role?.Name ?? assignment.Role?.Code})"));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Who reviews somebody with no manager.
    ///
    /// Falls to the Organisation administrator, and never to the subject themselves — which
    /// is why the result is checked against the subject id by the caller.
    /// </summary>
    private async Task<Guid?> ResolveFallbackReviewerAsync(
        Guid tenantId, Guid subjectId, CancellationToken cancellationToken)
    {
        var admin = await users.FindTenantAdminAsync(tenantId, cancellationToken);

        return admin is null || admin.Id == subjectId ? null : admin.Id;
    }

    private static string BuildSnapshot(User subject)
    {
        var now = DateTimeOffset.UtcNow;

        var roles = subject.UserRoles
            .Where(assignment => assignment.IsEffective(now))
            .Select(assignment => assignment.Role?.Code ?? assignment.RoleId.ToString());

        var scopes = subject.DataScopes
            .Where(scope => scope.IsEffective(now))
            .Select(scope => scope.ToClaimValue());

        return string.Join("; ", roles.Concat(scopes));
    }

    private async Task NotifyReviewerAsync(
        AccessReview review, User reviewer, CancellationToken cancellationToken)
    {
        var businessUnit = await businessUnits.GetByIdAsync(review.BusinessUnitId, cancellationToken);
        if (businessUnit is null)
        {
            return;
        }

        var tenant = await tenants.GetByIdAsync(review.TenantId, cancellationToken);

        await notifications.SendAccessReviewReminderAsync(
            reviewer, tenant, businessUnit, review.ReviewNumber, review.ReviewDueAtUtc,
            // FROM CONFIGURATION. This was the literal route, so renaming the page in Angular
            // would have left every reminder e-mail pointing at a 404 - and nothing would have
            // failed to compile to say so.
            $"{clientApp.Value.AccessReviewPath}?id={review.Id}",
            cancellationToken);
    }

    /// <summary>One assignment that needs recertifying.</summary>
    private sealed record ReviewCandidate(
        Guid UserId, Guid? ManagerUserId, Guid? UserRoleId, Guid? RoleId, string Snapshot);
}
