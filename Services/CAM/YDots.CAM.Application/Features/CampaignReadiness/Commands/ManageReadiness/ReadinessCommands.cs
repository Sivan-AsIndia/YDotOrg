using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.CampaignReadiness.DTOs;
using YDots.CAM.Application.Features.CampaignReadiness.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.CampaignReadiness.Commands.ManageReadiness;

/// <summary>Adds a check to a campaign checklist.</summary>
public sealed record CreateReadinessCheckCommand(Guid CampaignId, CreateReadinessCheckRequest Request);

/// <summary>Edits a Pending check.</summary>
public sealed record UpdateReadinessCheckCommand(Guid CheckId, UpdateReadinessCheckRequest Request);

/// <summary>Signs a check off as passed.</summary>
public sealed record PassReadinessCheckCommand(Guid CheckId, ReadinessVerdictRequest Request);

/// <summary>Records a check as failed.</summary>
public sealed record FailReadinessCheckCommand(Guid CheckId, ReadinessVerdictRequest Request);

/// <summary>Raises a blocker against a check.</summary>
public sealed record AssignReadinessBlockerCommand(Guid CheckId, AssignReadinessBlockerRequest Request);

/// <summary>Clears a blocker.</summary>
public sealed record ResolveReadinessBlockerCommand(Guid BlockerId, ResolveReadinessBlockerRequest Request);

/// <summary>Sends a campaign back to Draft from the readiness screen.</summary>
public sealed record ReturnCampaignToDraftCommand(Guid CampaignId, ReturnCampaignToDraftRequest Request);

/// <summary>Removes a Pending check from a campaign's checklist.</summary>
public sealed record DeleteReadinessCheckCommand(Guid CheckId, ReadinessVerdictRequest Request);

/// <summary>
/// The campaign readiness checklist.
///
/// THE CHECKLIST IS A GATE, NOT A NOTE. Its whole purpose is to stop a campaign going live
/// without its payment configuration, its consent wording or its tracking in place, so the
/// rules here are about who may declare what:
///
///   - A check may only be judged while it is PENDING. Re-opening a decided check goes through
///     raising a blocker, which leaves a record of why.
///   - A check with an OPEN BLOCKER cannot be passed. Signing off something a colleague has
///     flagged is exactly what the blocker exists to prevent.
///   - Raising a blocker FAILS the check it is raised against, so the checklist total reflects
///     reality immediately rather than after somebody remembers to change the status too.
///
/// WHAT LEFT THIS SLICE. It used to contain ApproveCampaignReadinessCommand and
/// RequestCampaignReadinessApprovalCommand, which moved a CAMPAIGN through Submitted to
/// Approved from inside the readiness feature - a second, parallel approval path with its own
/// copy of the status rules and NO segregation-of-duties check. Campaign approval now happens
/// in one place, <c>CampaignLifecycleCommandHandler</c>, which checks the readiness gate as
/// part of activation. What remains here is returning a campaign to Draft, which is genuinely a
/// readiness decision.
/// </summary>
public sealed class ReadinessCommandHandler(
    ICampaignReadinessRepository readiness,
    ICampaignRepository campaigns,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ReadinessCheckDetailResponse>> HandleAsync(
        CreateReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<ReadinessCheckDetailResponse>(
                Error.NotFound("That campaign was not found."));
        }

        var name = command.Request.CheckName.Trim();

        // Two checks with the same name on one campaign make the checklist unreadable, and
        // make "has the payment check passed?" ambiguous.
        if (await readiness.CheckNameExistsAsync(campaign.Id, name, null, cancellationToken))
        {
            return Result.Failure<ReadinessCheckDetailResponse>(
                Error.Duplicate($"A check named '{name}' already exists on this campaign."));
        }

        var check = command.Request.ToEntity(campaign);

        await readiness.AddAsync(check, cancellationToken);

        await audit.WriteAsync(
            ReadinessAuditActionCodes.Created, nameof(CampaignReadinessCheck), check.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return check.ToDetailResponse(clock.TodayUtc, PermittedActions(check));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadCheckAsync(
            command.CheckId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var check = loaded.Value!;

        if (check.Status != ReadinessCheckStatus.Pending)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Pending check can be edited. This one is {check.Status}."));
        }

        var name = command.Request.CheckName.Trim();

        if (await readiness.CheckNameExistsAsync(check.CampaignId, name, check.Id, cancellationToken))
        {
            return Result.Failure<OutcomeResponse>(
                Error.Duplicate($"A check named '{name}' already exists on this campaign."));
        }

        command.Request.ApplyTo(check);

        await audit.WriteAsync(
            ReadinessAuditActionCodes.Updated, nameof(CampaignReadinessCheck), check.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(check, "Readiness check updated.");
    }

    /// <summary>
    /// Signs a check off as passed.
    ///
    /// REFUSED WHILE A BLOCKER IS OPEN, which is the rule that gives a blocker its meaning: it
    /// is not a note, it is a hold.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        PassReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadCheckAsync(
            command.CheckId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var check = loaded.Value!;

        // ---- A FAILED CHECK IS NOT A DEAD END --------------------------------------------------
        //
        // This accepted Pending and nothing else, and a check recorded as Failed with no blocker
        // against it had no way out of Failed at all. Its permitted actions were View and
        // AddBlocker; Edit is refused on anything that is not Pending; Pass answered
        // 409 "Only a Pending check can be passed"; and there was no blocker to resolve. The only
        // escape was to raise a blocker on it and immediately resolve it, which is a workaround
        // that also writes a fictional obstacle into the audit trail.
        //
        // That mattered because failing a check is the ordinary thing to do when the verification
        // has not been done yet - and re-verifying it afterwards is the ordinary thing to do next.
        // A campaign whose readiness check cannot leave Failed can never launch.
        //
        // APPROVED IS STILL NOT RE-SIGNABLE: passing an already-Passed check is a no-op dressed
        // up as a decision, and it is refused below.
        //
        // THE BLOCKER RULE IS UNCHANGED and is what keeps this honest. A check with an open
        // blocker cannot be passed from any state, so "raise a blocker, then pass it anyway" is
        // still impossible; the blocker has to be resolved first, on the record, by somebody.
        if (check.Status == ReadinessCheckStatus.Passed)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This check has already been passed."));
        }

        if (check.HasOpenBlockers)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This check has an unresolved blocker. Clear it before signing the check off."));
        }

        check.Status = ReadinessCheckStatus.Passed;

        if (!string.IsNullOrWhiteSpace(command.Request.Notes))
        {
            check.Notes = command.Request.Notes.Trim();
        }

        await audit.WriteAsync(
            ReadinessAuditActionCodes.Passed, nameof(CampaignReadinessCheck), check.Id,
            command.Request.Notes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(check, "Readiness check passed.");
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        FailReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadCheckAsync(
            command.CheckId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var check = loaded.Value!;

        if (check.Status != ReadinessCheckStatus.Pending)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Pending check can be failed. This one is {check.Status}."));
        }

        check.Status = ReadinessCheckStatus.Failed;

        if (!string.IsNullOrWhiteSpace(command.Request.Notes))
        {
            check.Notes = command.Request.Notes.Trim();
        }

        await audit.WriteAsync(
            ReadinessAuditActionCodes.Failed, nameof(CampaignReadinessCheck), check.Id,
            command.Request.Notes, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildOutcome(check, "Readiness check recorded as failed.");
    }

    /// <summary>
    /// Raises a blocker.
    ///
    /// IT ALSO FAILS THE CHECK, which is what the original did and is right: a check somebody
    /// has flagged as blocked is not pending, it is failing. Doing both in one operation is
    /// what keeps the checklist totals honest without relying on the operator to remember a
    /// second step.
    /// </summary>
    public async Task<Result<ReadinessBlockerResponse>> HandleAsync(
        AssignReadinessBlockerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadCheckAsync(
            command.CheckId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReadinessBlockerResponse>(loaded.Error!);
        }

        var check = loaded.Value!;

        // One open blocker at a time. Several would make "is this check blocked?" a count
        // rather than a yes or no, and nothing downstream wants the count.
        if (check.HasOpenBlockers)
        {
            return Result.Failure<ReadinessBlockerResponse>(
                Error.Duplicate("This check already has an unresolved blocker."));
        }

        var blocker = new CampaignReadinessBlocker
        {
            CampaignReadinessCheckId = check.Id,
            OwnerUserId = command.Request.OwnerUserId,
            BlockerNote = command.Request.BlockerNote.Trim(),
            IsResolved = false
        };

        await readiness.AddBlockerAsync(blocker, cancellationToken);

        check.Status = ReadinessCheckStatus.Failed;

        await audit.WriteAsync(
            ReadinessAuditActionCodes.BlockerAssigned, nameof(CampaignReadinessCheck), check.Id,
            command.Request.BlockerNote, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return blocker.ToResponse();
    }

    /// <summary>
    /// Clears a blocker.
    ///
    /// THE CHECK GOES BACK TO PENDING, NOT TO PASSED. Clearing the obstacle is not the same as
    /// verifying the thing: somebody still has to look at the check and sign it off, and
    /// auto-passing here would let a blocker be raised and cleared as a way of skipping the
    /// verification entirely.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ResolveReadinessBlockerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var blocker = await readiness.GetBlockerAsync(command.BlockerId, cancellationToken);
        if (blocker is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That blocker was not found."));
        }

        if (blocker.IsResolved)
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition("That blocker has already been resolved."));
        }

        blocker.IsResolved = true;
        blocker.ResolvedByUserId = currentUser.UserId;
        blocker.ResolvedAtUtc = clock.UtcNow;
        blocker.ResolutionNote = string.IsNullOrWhiteSpace(command.Request.ResolutionNote)
            ? null
            : command.Request.ResolutionNote.Trim();

        var check = blocker.ReadinessCheck;

        if (check is not null && !check.Blockers.Any(other => other.Id != blocker.Id && !other.IsResolved))
        {
            check.Status = ReadinessCheckStatus.Pending;
        }

        await audit.WriteAsync(
            ReadinessAuditActionCodes.BlockerResolved, nameof(CampaignReadinessBlocker), blocker.Id,
            command.Request.ResolutionNote, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return check is null
            ? new OutcomeResponse(blocker.Id, "Resolved", 0, "Blocker resolved.", [])
            : BuildOutcome(check, "Blocker resolved. The check is pending verification again.");
    }

    /// <summary>
    /// Sends a campaign back to Draft.
    ///
    /// The escape hatch when readiness turns up something that needs the campaign itself
    /// changed - a wrong target, wrong dates - rather than a check re-run. A REASON IS
    /// MANDATORY, because whoever submitted it will want to know why it came back.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ReturnCampaignToDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That campaign was not found."));
        }

        if (campaign.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // Only before it goes live. A campaign that is already Active has donations against it,
        // and dropping it back to Draft would let its target and dates be edited underneath money
        // that has already been taken.
        //
        // SCHEDULED IS INCLUDED, and it has to be. Approving a campaign now leaves it Scheduled
        // rather than Approved, so without this a returned-to-draft was unreachable for exactly
        // the campaigns the readiness screen is looking at - and a scheduled campaign is the one
        // most worth being able to pull back, because it is about to go live by itself.
        if (campaign.Status is not (CampaignStatus.Submitted
                                    or CampaignStatus.Approved
                                    or CampaignStatus.Scheduled))
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "Only a Submitted, Approved or Scheduled campaign can be returned to Draft. "
                + $"This one is {campaign.Status}."));
        }

        campaign.Status = CampaignStatus.Draft;

        // The previous approval no longer applies. Cleared so the segregation-of-duties check
        // starts from a clean slate on the next submission.
        campaign.SubmittedByUserId = null;
        campaign.SubmittedAtUtc = null;
        campaign.ApprovedByUserId = null;
        campaign.ApprovedAtUtc = null;

        // A lifecycle row as well as an audit row, so the history tab shows the campaign coming
        // back beside the submission that sent it - the two halves of one conversation.
        await campaigns.AddLifecycleActionAsync(
            new CampaignLifecycleAction
            {
                CampaignId = campaign.Id,
                ActionType = CampaignLifecycleActionType.ReturnToDraft,
                ActionStatus = CampaignLifecycleActionStatus.Completed,
                EffectiveAtUtc = clock.UtcNow,
                DetailedReason = command.Request.Reason.Trim(),
                RequestedByUserId = currentUser.UserId
            },
            cancellationToken);

        await audit.WriteAsync(
            ReadinessAuditActionCodes.ReturnedToDraft, nameof(Campaign), campaign.Id,
            command.Request.Reason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new OutcomeResponse(
            campaign.Id, campaign.Status.ToString(), campaign.Version,
            "Campaign returned to Draft.", []);
    }

    // =====================================================================================
    // Shared
    // =====================================================================================

    /// <summary>
    /// Removes a check from the checklist.
    ///
    /// PENDING ONLY, and that restriction is the whole point. A passed or failed check carries
    /// somebody's verdict - the record that a person looked at the payment configuration and said
    /// what they found - and deleting the check would destroy the answer along with the question.
    /// A check that turned out not to apply is removed before anyone judges it; one that has been
    /// judged and turned out to be wrong is re-opened by raising a blocker, which leaves a trail.
    ///
    /// A CHECK WITH BLOCKERS ON IT STAYS TOO. A blocker is somebody recording an obstacle against
    /// this campaign; deleting the check under it would take the obstacle out of the checklist
    /// without anyone having resolved it.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteReadinessCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadCheckAsync(
            command.CheckId, command.Request.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(loaded.Error!);
        }

        var check = loaded.Value!;

        if (check.Status != ReadinessCheckStatus.Pending)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Pending check can be deleted. This one is {check.Status}, so it carries "
                + "a verdict that would be destroyed with it."));
        }

        if (check.Blockers.Count > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "This check has blockers raised against it. Resolve them before removing it."));
        }

        await audit.WriteAsync(
            ReadinessAuditActionCodes.Deleted, nameof(CampaignReadinessCheck), check.Id,
            command.Request.Notes, cancellationToken);

        // Built while the check is still readable; the row goes on save.
        var outcome = BuildOutcome(check, "Readiness check deleted.");

        readiness.Remove(check);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return outcome;
    }

    private async Task<Result<CampaignReadinessCheck>> LoadCheckAsync(
        Guid checkId, long expectedVersion, CancellationToken cancellationToken)
    {
        var check = await readiness.GetByIdAsync(checkId, cancellationToken);

        if (check is null)
        {
            return Result.Failure<CampaignReadinessCheck>(
                Error.NotFound("That readiness check was not found."));
        }

        return check.Version == expectedVersion
            ? check
            : Result.Failure<CampaignReadinessCheck>(Error.Concurrency());
    }

    private OutcomeResponse BuildOutcome(CampaignReadinessCheck check, string message) =>
        new(check.Id, check.Status.ToString(), check.Version, message, PermittedActions(check));

    private IReadOnlyList<string> PermittedActions(CampaignReadinessCheck check) =>
        ReadinessMappingConfig.PermittedActionsFor(check, currentUser.HasPermission);
}
