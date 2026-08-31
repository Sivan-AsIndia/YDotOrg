using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Application.Features.Campaigns.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Campaigns.Commands.CampaignLifecycle;

/// <summary>Draft to Submitted.</summary>
public sealed record SubmitCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Submitted to Approved. Refused for the person who created or submitted it.</summary>
public sealed record ApproveCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Approved or Scheduled to Active. Refused while a required readiness check is outstanding.</summary>
public sealed record ActivateCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Active to Paused.</summary>
public sealed record PauseCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Paused to Active.</summary>
public sealed record ResumeCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Raises a close request against a running campaign. Moves it to Closing.</summary>
public sealed record RequestCloseCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>Approves an outstanding close request. Refused for the person who raised it.</summary>
public sealed record ApproveCloseCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>
/// Every campaign lifecycle transition, in one handler.
///
/// ONE CLASS RATHER THAN SEVEN, and the reason is that the seven MediatR handlers it replaces
/// were the same forty lines seven times: load, check the status, check the version, write a
/// lifecycle row, change the status, write an audit row, save. What actually differed was three
/// values - the status it must be in, the status it becomes, and the audit code. Those are now
/// the arguments to <see cref="TransitionAsync"/>, and the shared forty lines exist once.
///
/// THE TWO RULES THAT MAKE THIS MORE THAN BOOKKEEPING:
///
/// SEGREGATION OF DUTIES. Section 5.2 of the module brief: a Campaign Manager may not approve a
/// campaign they personally created or submitted, and the person who raises a close request may
/// not approve it. Both are enforced here rather than in the client, and both return a distinct
/// error code so the screen can say WHY rather than just "forbidden".
///
/// READINESS. A campaign cannot go Active while a required readiness check has not passed. The
/// refusal names the outstanding checks as field errors, so the operator is told what to go and
/// fix instead of being told no.
/// </summary>
public sealed class CampaignLifecycleCommandHandler(
    ICampaignRepository campaigns,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<CampaignSettings> campaignOptions,
    IUnitOfWork unitOfWork,
    ILogger<CampaignLifecycleCommandHandler> logger)
{
    private readonly CampaignSettings _settings = campaignOptions.Value;

    // =====================================================================================
    // Submit
    // =====================================================================================

    /// <summary>
    /// Draft to Submitted.
    ///
    /// THE TIERED APPROVAL RULE LIVES HERE. Section 5.1: when a Super Admin submits, the
    /// campaign can be approved and scheduled in the same step. It is a SETTING rather than a
    /// hard-coded branch, because an Organisation that wants a second pair of eyes on
    /// everything - root user included - is making a legitimate choice.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        SubmitCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForTransitionAsync(
            command.CampaignId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        if (campaign.Status != CampaignStatus.Draft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Draft campaign can be submitted. This one is {campaign.Status}."));
        }

        var now = clock.UtcNow;

        campaign.Status = CampaignStatus.Submitted;
        campaign.SubmittedByUserId = currentUser.UserId;
        campaign.SubmittedAtUtc = now;

        await RecordLifecycleAsync(
            campaign, CampaignLifecycleActionType.Activate, CampaignLifecycleActionStatus.Completed,
            command.Request, now, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.CampaignSubmitted, nameof(Campaign), campaign.Id,
            command.Request.DetailedReason, cancellationToken);

        // NO AUTO-APPROVAL HERE, FOR ANYBODY - INCLUDING A PLATFORM ADMINISTRATOR.
        //
        // This block used to promote a super admin's submission straight to Approved, stamping the
        // same user id into SubmittedByUserId and ApprovedByUserId with an identical timestamp. It
        // was one person completing both halves of an approval, which is the single rule this
        // platform says cannot be granted away, and it was reachable by the one account that
        // operates in every organisation and is scrutinised least.
        //
        // It was also inconsistent with its own neighbours: approve-close, tracking assets, donor
        // records and refund cases all refuse the same super admin with
        // SEGREGATION_OF_DUTIES_VIOLATION. Only campaign submission granted the exemption, so it
        // read as an oversight rather than a policy.
        //
        // A platform administrator can still approve a campaign - just not one they submitted
        // themselves, exactly like everybody else. The CampaignSettings flag that switched this on
        // has been removed rather than defaulted to false, because a setting that can reinstate the
        // hole is itself the hole.
        var message = "Campaign submitted for approval.";

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, message, cancellationToken);
    }

    // =====================================================================================
    // Approve
    // =====================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApproveCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForTransitionAsync(
            command.CampaignId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        if (campaign.Status != CampaignStatus.Submitted)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Submitted campaign can be approved. This one is {campaign.Status}."));
        }

        // THE SEGREGATION-OF-DUTIES CHECK. It is recorded as a DENIED audit row rather than
        // simply refused, because an attempt to approve one's own work is exactly the pattern a
        // later review wants to see.
        if (!campaign.CanBeApprovedBy(currentUser.UserId))
        {
            await audit.WriteAsync(
                AuditActionCodes.CampaignApproved, nameof(Campaign), campaign.Id,
                AuditResult.Denied, "Attempted to approve a campaign they created or submitted.",
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot approve a campaign you created or submitted. Ask a colleague to review it."));
        }

        var now = clock.UtcNow;

        campaign.Status = CampaignStatus.Approved;
        campaign.ApprovedByUserId = currentUser.UserId;
        campaign.ApprovedAtUtc = now;

        await audit.WriteAsync(
            AuditActionCodes.CampaignApproved, nameof(Campaign), campaign.Id,
            command.Request.DetailedReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, "Campaign approved.", cancellationToken);
    }

    // =====================================================================================
    // Activate
    // =====================================================================================

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ActivateCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForTransitionAsync(
            command.CampaignId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        // Scheduled is accepted alongside Approved: a campaign whose activation is automatic
        // sits in Scheduled until its start date, and an operator may legitimately bring it
        // forward by hand.
        if (campaign.Status is not (CampaignStatus.Approved or CampaignStatus.Scheduled))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only an Approved or Scheduled campaign can be activated. This one is {campaign.Status}."));
        }

        var today = clock.TodayUtc;

        if (campaign.EndDate < today)
        {
            return Result.Failure<OutcomeResponse>(Error.CampaignWindowClosed(
                $"This campaign ended on {campaign.EndDate:yyyy-MM-dd}. Extend its dates before activating it."));
        }

        // THE READINESS GATE. The refusal names the outstanding checks so the operator is told
        // what to go and fix, rather than simply being told no.
        var outstanding = await campaigns.GetOutstandingRequiredChecksAsync(campaign.Id, cancellationToken);

        if (outstanding.Count > 0 && !_settings.AllowLaunchWithOutstandingChecks)
        {
            return Result.Failure<OutcomeResponse>(Error.ReadinessIncomplete(
                $"{outstanding.Count} required readiness check(s) have not passed.",
                [.. outstanding.Select(check =>
                    new ValidationError(check.CheckName, $"{check.Category}: {check.SuccessCriteria}"))]));
        }

        var now = clock.UtcNow;

        campaign.Status = CampaignStatus.Active;

        await RecordLifecycleAsync(
            campaign, CampaignLifecycleActionType.Activate, CampaignLifecycleActionStatus.Completed,
            command.Request, now, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.CampaignActivated, nameof(Campaign), campaign.Id,
            command.Request.ReasonCategory, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, "Campaign activated.", cancellationToken);
    }

    // =====================================================================================
    // Pause and resume
    // =====================================================================================

    public Task<Result<OutcomeResponse>> HandleAsync(
        PauseCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TransitionAsync(
            command.CampaignId,
            command.Request,
            requiredStatus: [CampaignStatus.Active],
            newStatus: CampaignStatus.Paused,
            actionType: CampaignLifecycleActionType.Pause,
            auditCode: AuditActionCodes.CampaignPaused,
            refusal: "Only an Active campaign can be paused.",
            successMessage: "Campaign paused.",
            cancellationToken);
    }

    public Task<Result<OutcomeResponse>> HandleAsync(
        ResumeCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TransitionAsync(
            command.CampaignId,
            command.Request,
            requiredStatus: [CampaignStatus.Paused],
            newStatus: CampaignStatus.Active,
            actionType: CampaignLifecycleActionType.Resume,
            auditCode: AuditActionCodes.CampaignResumed,
            refusal: "Only a Paused campaign can be resumed.",
            successMessage: "Campaign resumed.",
            cancellationToken);
    }

    // =====================================================================================
    // Close
    // =====================================================================================

    /// <summary>
    /// Raises a close request.
    ///
    /// The campaign moves to Closing rather than straight to Closed, which is the whole point of
    /// a two-step close: donations stop being solicited while the request waits for a second
    /// person, and the campaign is visibly in that state rather than silently still running.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RequestCloseCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForTransitionAsync(
            command.CampaignId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        if (campaign.Status is not (CampaignStatus.Active or CampaignStatus.Paused))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only an Active or Paused campaign can be closed. This one is {campaign.Status}."));
        }

        if (await campaigns.GetPendingCloseRequestAsync(campaign.Id, cancellationToken) is not null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Duplicate("A close request is already pending for this campaign."));
        }

        // The two reason fields are mandatory HERE and optional on the other transitions, which
        // is why the check is in this method rather than on the shared request type: closing a
        // campaign is the one transition somebody will be asked to justify months later.
        var missing = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(command.Request.ReasonCategory))
        {
            missing.Add(new ValidationError(
                nameof(command.Request.ReasonCategory), "Choose a reason for closing the campaign."));
        }

        if (string.IsNullOrWhiteSpace(command.Request.DetailedReason))
        {
            missing.Add(new ValidationError(
                nameof(command.Request.DetailedReason), "Explain why the campaign is being closed."));
        }

        if (missing.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(
                Error.Validation("A close request needs a reason.", missing));
        }

        var now = clock.UtcNow;

        campaign.Status = CampaignStatus.Closing;

        await RecordLifecycleAsync(
            campaign, CampaignLifecycleActionType.RequestClose, CampaignLifecycleActionStatus.Pending,
            command.Request, now, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.CampaignCloseRequested, nameof(Campaign), campaign.Id,
            command.Request.DetailedReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, "Close request submitted for approval.", cancellationToken);
    }

    /// <summary>Approves an outstanding close request. Refused for the person who raised it.</summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ApproveCloseCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForTransitionAsync(
            command.CampaignId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        var closeRequest = await campaigns.GetPendingCloseRequestAsync(campaign.Id, cancellationToken);

        if (closeRequest is null)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "There is no pending close request for this campaign."));
        }

        // The independence rule again, this time against the REQUEST rather than the campaign:
        // whoever raised the close cannot be the one who approves it.
        if (!closeRequest.CanBeApprovedBy(currentUser.UserId))
        {
            await audit.WriteAsync(
                AuditActionCodes.CampaignCloseApproved, nameof(Campaign), campaign.Id,
                AuditResult.Denied, "Attempted to approve their own close request.", cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<OutcomeResponse>(Error.SegregationOfDuties(
                "You cannot approve a close request you raised. Ask a colleague to review it."));
        }

        var now = clock.UtcNow;

        closeRequest.ActionStatus = CampaignLifecycleActionStatus.Completed;
        closeRequest.ApprovedByUserId = currentUser.UserId;
        closeRequest.ApprovedAtUtc = now;

        campaign.Status = CampaignStatus.Closed;

        await audit.WriteAsync(
            AuditActionCodes.CampaignCloseApproved, nameof(Campaign), campaign.Id,
            command.Request.DetailedReason ?? closeRequest.DetailedReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Campaign {CampaignId} closed. Requested by {RequestedBy}, approved by {ApprovedBy}.",
            campaign.Id, closeRequest.RequestedByUserId, currentUser.UserId);

        return await BuildOutcomeAsync(campaign, "Campaign closed.", cancellationToken);
    }

    // =====================================================================================
    // The shared transition
    // =====================================================================================

    /// <summary>
    /// The status change every simple transition performs.
    ///
    /// Pause and resume are the two that need nothing beyond it. Submit, approve, activate and
    /// the close pair each add a rule of their own and are written out above rather than being
    /// forced through a parameter that would hide the interesting half.
    /// </summary>
    private async Task<Result<OutcomeResponse>> TransitionAsync(
        Guid campaignId,
        CampaignLifecycleRequest request,
        IReadOnlyList<CampaignStatus> requiredStatus,
        CampaignStatus newStatus,
        CampaignLifecycleActionType actionType,
        string auditCode,
        string refusal,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadForTransitionAsync(campaignId, request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var campaign = loaded.Value!;

        if (!requiredStatus.Contains(campaign.Status))
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition($"{refusal} This one is {campaign.Status}."));
        }

        var now = clock.UtcNow;

        campaign.Status = newStatus;

        await RecordLifecycleAsync(
            campaign, actionType, CampaignLifecycleActionStatus.Completed, request, now, cancellationToken);

        await audit.WriteAsync(
            auditCode, nameof(Campaign), campaign.Id, request.DetailedReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, successMessage, cancellationToken);
    }

    /// <summary>
    /// Loads a campaign and checks the caller is allowed to act on the version they think they
    /// are acting on.
    ///
    /// Both checks in one place, because every one of the seven transitions needs both and in
    /// the same order - and a transition that checked the status before the version would
    /// report a stale screen as an invalid transition, which sends the operator looking in
    /// entirely the wrong place.
    /// </summary>
    private async Task<Result<Campaign>> LoadForTransitionAsync(
        Guid campaignId, long expectedVersion, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetByIdAsync(campaignId, cancellationToken);

        if (campaign is null)
        {
            return Result.Failure<Campaign>(Error.NotFound("That campaign was not found."));
        }

        return campaign.Version == expectedVersion
            ? campaign
            : Result.Failure<Campaign>(Error.Concurrency());
    }

    private async Task RecordLifecycleAsync(
        Campaign campaign,
        CampaignLifecycleActionType actionType,
        CampaignLifecycleActionStatus actionStatus,
        CampaignLifecycleRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await campaigns.AddLifecycleActionAsync(
            new CampaignLifecycleAction
            {
                CampaignId = campaign.Id,
                ActionType = actionType,
                ActionStatus = actionStatus,
                EffectiveAtUtc = now,
                ReasonCategory = request.ReasonCategory?.Trim(),
                DetailedReason = request.DetailedReason?.Trim(),
                CommunicationImpact = request.CommunicationImpact?.Trim(),
                ClosureSummary = request.ClosureSummary?.Trim(),
                RequestedByUserId = currentUser.UserId
            },
            cancellationToken);
    }

    private async Task<OutcomeResponse> BuildOutcomeAsync(
        Campaign campaign, string message, CancellationToken cancellationToken)
    {
        var outstanding = await campaigns.GetOutstandingRequiredChecksAsync(campaign.Id, cancellationToken);
        var pendingClose = await campaigns.GetPendingCloseRequestAsync(campaign.Id, cancellationToken);

        return new OutcomeResponse(
            campaign.Id,
            campaign.Status.ToString(),
            campaign.Version,
            message,
            CampaignMappingConfig.PermittedActionsFor(
                campaign, currentUser.UserId, currentUser.HasPermission,
                outstanding.Count > 0, pendingClose is not null));
    }
}
