using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.BudgetTargetPlans.DTOs;
using YDots.CAM.Application.Features.BudgetTargetPlans.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.BudgetTargetPlans.Commands.ManageBudgetPlan;

/// <summary>Creates a plan and its first draft version.</summary>
public sealed record AllocateBudgetPlanCommand(AllocateBudgetPlanRequest Request);

/// <summary>Appends a new draft version to an existing plan.</summary>
public sealed record ReviseBudgetPlanCommand(Guid PlanId, ReviseBudgetPlanRequest Request);

/// <summary>Edits a draft version in place.</summary>
public sealed record UpdateBudgetPlanVersionCommand(Guid VersionId, UpdateBudgetPlanVersionRequest Request);

/// <summary>Sends a draft version for approval.</summary>
public sealed record SubmitBudgetPlanVersionCommand(Guid VersionId, SubmitBudgetPlanVersionRequest Request);

/// <summary>Approves a submitted version, making it the plan's committed figures.</summary>
public sealed record ApproveBudgetPlanVersionCommand(Guid VersionId, BudgetPlanDecisionRequest Request);

/// <summary>Rejects a submitted version, returning it for revision.</summary>
public sealed record RejectBudgetPlanVersionCommand(Guid VersionId, BudgetPlanDecisionRequest Request);

/// <summary>
/// Budget and target plans.
///
/// A PLAN'S FIGURES ARE WHAT A CAMPAIGN IS RUN TO, so the rules here are about who may commit an
/// organisation to what:
///
///   - REVISING APPENDS. An approved version is never edited; a revision creates the next version
///     and the approved one stays exactly as approved until a newer one replaces it. Somebody
///     asking "what were we working to in August?" gets an answer.
///   - ONLY A DRAFT MAY BE EDITED. Everything else is a record of a decision, and editing those in
///     place would rewrite an agreement after the fact.
///   - THE SUBMITTER CANNOT APPROVE. Not a formality: a budget is where an organisation commits its
///     money, and one person doing both ends is exactly the control an auditor looks for. Checked
///     against the stored submitter, not against anything the request claims.
///   - APPROVING SUPERSEDES. The previously approved version moves to Superseded in the same
///     transaction, so there is never a moment when two versions of one plan both count.
/// </summary>
public sealed class BudgetPlanCommandHandler(
    IBudgetTargetPlanRepository plans,
    ICampaignRepository campaigns,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    // =============================================================================================
    // Allocate
    // =============================================================================================

    public async Task<Result<BudgetPlanDetailResponse>> HandleAsync(
        AllocateBudgetPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var campaign = await campaigns.GetByIdAsync(request.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<BudgetPlanDetailResponse>(
                Error.NotFound("That campaign was not found."));
        }

        // A CLOSED CAMPAIGN TAKES NO NEW PLANS. Budgeting for something that has finished is
        // either a mistake or an attempt to move figures into a period that is already reported.
        if (campaign.Status is CampaignStatus.Closed or CampaignStatus.Cancelled)
        {
            return Result.Failure<BudgetPlanDetailResponse>(Error.InvalidTransition(
                $"A budget plan cannot be allocated to a {campaign.Status} campaign."));
        }

        var period = request.PlanPeriod.Trim();
        var dimension = request.TargetDimension.Trim();

        var duplicate = await plans.FindDuplicateAsync(
            campaign.Id, period, dimension, null, cancellationToken);

        if (duplicate is not null)
        {
            return Result.Failure<BudgetPlanDetailResponse>(Error.Duplicate(
                $"Plan {duplicate.Code} already covers {period} for {dimension} on this campaign. "
                + "Revise that plan rather than allocating a second one."));
        }

        var plan = request.ToEntity(campaign);
        plan.Code = await plans.NextCodeAsync(cancellationToken);

        // The campaign's currency unless the caller names another. See the request DTO for why
        // this is optional rather than required.
        var currencyId = request.CurrencyId ?? campaign.CurrencyId;

        var version = request.ToFirstVersion(plan, currencyId);
        plan.Versions.Add(version);

        await plans.AddAsync(plan, cancellationToken);

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Allocated, nameof(BudgetTargetPlan), plan.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return plan.ToDetailResponse(
            campaign.Code, campaign.Name, string.Empty, 0m, PermittedActions(plan));
    }

    // =============================================================================================
    // Revise
    // =============================================================================================

    /// <summary>
    /// Appends a new draft version.
    ///
    /// REFUSED WHILE A VERSION IS AWAITING A DECISION. Two live drafts on one plan means an
    /// approver is looking at figures that have already been superseded by a colleague, and
    /// whichever they approve, somebody's work is silently discarded.
    /// </summary>
    public async Task<Result<BudgetPlanDetailResponse>> HandleAsync(
        ReviseBudgetPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<BudgetPlanDetailResponse>(
                Error.NotFound("That budget plan was not found."));
        }

        if (plan.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<BudgetPlanDetailResponse>(Error.Concurrency());
        }

        var pending = plan.Versions.FirstOrDefault(version =>
            version.ApprovalState is PlanApprovalState.Draft or PlanApprovalState.Submitted);

        if (pending is not null)
        {
            return Result.Failure<BudgetPlanDetailResponse>(Error.InvalidTransition(
                $"Version v{pending.VersionNumber} is still {pending.ApprovalState}. "
                + "Finish or withdraw it before revising the plan again."));
        }

        var campaign = await campaigns.GetByIdAsync(plan.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<BudgetPlanDetailResponse>(
                Error.NotFound("The campaign behind this plan was not found."));
        }

        var currencyId = command.Request.CurrencyId
            ?? plan.ApprovedVersion?.CurrencyId
            ?? campaign.CurrencyId;

        var version = command.Request.ToNextVersion(plan, currencyId);
        plan.Versions.Add(version);

        await plans.AddVersionAsync(version, cancellationToken);

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Revised, nameof(BudgetTargetPlanVersion), version.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return plan.ToDetailResponse(
            campaign.Code, campaign.Name, string.Empty, 0m, PermittedActions(plan));
    }

    // =============================================================================================
    // Edit a draft
    // =============================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateBudgetPlanVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadVersionAsync(
            command.VersionId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var version = loaded.Value!;

        if (!version.IsEditable)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Draft version can be edited. Version v{version.VersionNumber} is "
                + $"{version.ApprovalState}. Revise the plan to change the figures."));
        }

        command.Request.ApplyTo(version, command.Request.CurrencyId ?? version.CurrencyId);

        // The owner may be reassigned while the plan is still a draft.
        if (command.Request.OwnerUserId is { } owner && owner != Guid.Empty)
        {
            version.Plan.OwnerUserId = owner;
        }

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.VersionUpdated, nameof(BudgetTargetPlanVersion), version.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(version, "Budget plan version updated.");
    }

    // =============================================================================================
    // Submit
    // =============================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        SubmitBudgetPlanVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadVersionAsync(
            command.VersionId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var version = loaded.Value!;

        if (version.ApprovalState != PlanApprovalState.Draft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Draft version can be submitted. Version v{version.VersionNumber} is "
                + $"{version.ApprovalState}."));
        }

        // A PLAN THAT INTENDS TO SPEND AND RAISE NOTHING IS NOT A PLAN. Caught at submission rather
        // than at allocation, because a half-filled draft is a legitimate work in progress.
        if (version.TargetAmount == 0m && version.BudgetAmount == 0m)
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "A plan with no target and no budget has nothing to approve. "
                + "Enter the figures before submitting it."));
        }

        version.ApprovalState = PlanApprovalState.Submitted;
        version.SubmittedByUserId = currentUser.UserId;
        version.SubmittedAtUtc = clock.UtcNow;
        version.EffectiveAtUtc = clock.UtcNow;
        version.DecisionReason = Clean(command.Request.Note);

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Submitted, nameof(BudgetTargetPlanVersion), version.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(version, $"Version v{version.VersionNumber} submitted for approval.");
    }

    // =============================================================================================
    // Approve
    // =============================================================================================

    /// <summary>
    /// Approves a submitted version.
    ///
    /// THE SUBMITTER CANNOT APPROVE, and the previously approved version is superseded in the same
    /// transaction. Doing the supersede separately - or leaving it to a background job - would open
    /// a window in which a campaign's committed budget was the sum of two versions of the same plan.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApproveBudgetPlanVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadVersionAsync(
            command.VersionId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var version = loaded.Value!;

        if (version.ApprovalState != PlanApprovalState.Submitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Submitted version can be approved. Version v{version.VersionNumber} is "
                + $"{version.ApprovalState}."));
        }

        if (version.SubmittedByUserId == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You submitted this version, so it needs somebody else to approve it."));
        }

        // The version being replaced. Moved to Superseded rather than deleted: the figures a
        // decision was taken against have to stay readable.
        var superseded = version.Plan.Versions.FirstOrDefault(other =>
            other.Id != version.Id && other.ApprovalState == PlanApprovalState.Approved);

        if (superseded is not null)
        {
            superseded.ApprovalState = PlanApprovalState.Superseded;
            superseded.EffectiveAtUtc = clock.UtcNow;

            await audit.WriteAsync(
                BudgetPlanAuditActionCodes.Superseded, nameof(BudgetTargetPlanVersion), superseded.Id,
                cancellationToken: cancellationToken);
        }

        version.ApprovalState = PlanApprovalState.Approved;
        version.ApprovedByUserId = currentUser.UserId;
        version.ApprovedAtUtc = clock.UtcNow;
        version.EffectiveAtUtc = clock.UtcNow;
        version.DecisionReason = Clean(command.Request.Reason);

        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Approved, nameof(BudgetTargetPlanVersion), version.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = superseded is null
            ? $"Version v{version.VersionNumber} approved and now in force."
            : $"Version v{version.VersionNumber} approved. Version v{superseded.VersionNumber} "
              + "has been superseded and no longer counts toward the campaign totals.";

        return BuildOutcome(version, message);
    }

    // =============================================================================================
    // Reject
    // =============================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        RejectBudgetPlanVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A REASON IS REQUIRED. Without one the submitter has nothing to act on and resubmits the
        // same figures, which wastes the approver's time as much as theirs.
        if (string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "Say why the version is being rejected, so it can be revised."));
        }

        var loaded = await LoadVersionAsync(
            command.VersionId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var version = loaded.Value!;

        if (version.ApprovalState != PlanApprovalState.Submitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Submitted version can be rejected. Version v{version.VersionNumber} is "
                + $"{version.ApprovalState}."));
        }

        if (version.SubmittedByUserId == currentUser.UserId)
        {
            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You submitted this version, so somebody else has to decide on it."));
        }

        version.ApprovalState = PlanApprovalState.Rejected;
        version.DecisionReason = command.Request.Reason!.Trim();
        version.EffectiveAtUtc = clock.UtcNow;

        // NOT recorded as an approval. ApprovedByUserId means "this person put these figures into
        // force"; filling it in on a rejection would make the audit log say the opposite of what
        // happened.
        await audit.WriteAsync(
            BudgetPlanAuditActionCodes.Rejected, nameof(BudgetTargetPlanVersion), version.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(
            version, $"Version v{version.VersionNumber} rejected and returned for revision.");
    }

    // =============================================================================================
    // Internals
    // =============================================================================================

    private async Task<Result<BudgetTargetPlanVersion>> LoadVersionAsync(
        Guid versionId, long expectedVersion, CancellationToken cancellationToken)
    {
        var version = await plans.GetVersionAsync(versionId, cancellationToken);

        if (version is null)
        {
            return Result.Failure<BudgetTargetPlanVersion>(
                Error.NotFound("That budget plan version was not found."));
        }

        if (version.Version != expectedVersion)
        {
            return Result.Failure<BudgetTargetPlanVersion>(Error.Concurrency());
        }

        return version;
    }

    private OutcomeResponse BuildOutcome(BudgetTargetPlanVersion version, string message) =>
        new(version.Id,
            version.ApprovalState.ToString(),
            version.Version,
            message,
            PermittedActions(version));

    /// <summary>
    /// What this caller may do to a plan next.
    ///
    /// COMPUTED SERVER-SIDE so the buttons a screen draws and the rules the handler enforces cannot
    /// drift apart. A screen that drew an Approve button the server would refuse teaches people to
    /// distrust the buttons.
    /// </summary>
    private IReadOnlyList<string> PermittedActions(BudgetTargetPlan plan)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.BudgetPlansView))
        {
            actions.Add("View");
        }

        var pending = plan.Versions.Any(version =>
            version.ApprovalState is PlanApprovalState.Draft or PlanApprovalState.Submitted);

        if (!pending && currentUser.HasPermission(PermissionCodes.BudgetPlansRevise))
        {
            actions.Add("Revise");
        }

        if (currentUser.HasPermission(PermissionCodes.BudgetPlansExport))
        {
            actions.Add("Export");
        }

        return actions;
    }

    private IReadOnlyList<string> PermittedActions(BudgetTargetPlanVersion version)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.BudgetPlansView))
        {
            actions.Add("View");
        }

        if (version.IsEditable && currentUser.HasPermission(PermissionCodes.BudgetPlansRevise))
        {
            actions.Add("Edit");
        }

        if (version.ApprovalState == PlanApprovalState.Draft
            && currentUser.HasPermission(PermissionCodes.BudgetPlansSubmit))
        {
            actions.Add("Submit");
        }

        // THE SUBMITTER IS EXCLUDED HERE TOO, not only in the handler. Drawing an Approve button
        // for the person who submitted it and then refusing the click is worse than not drawing it:
        // it looks like a fault rather than a control.
        var isSubmitter = version.SubmittedByUserId == currentUser.UserId;

        if (version.ApprovalState == PlanApprovalState.Submitted && !isSubmitter)
        {
            if (currentUser.HasPermission(PermissionCodes.BudgetPlansApprove))
            {
                actions.Add("Approve");
            }

            if (currentUser.HasPermission(PermissionCodes.BudgetPlansReject))
            {
                actions.Add("Reject");
            }
        }

        return actions;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
