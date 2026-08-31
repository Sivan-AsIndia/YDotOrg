using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Attribution.DTOs;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Features.Attribution.Commands.ManageAttribution;

/// <summary>Asks for a donation's attribution to be looked at again.</summary>
public sealed record RequestAttributionCorrectionCommand(RequestAttributionCorrectionRequest Request);

/// <summary>Closes a correction request, recording what was decided.</summary>
public sealed record ResolveAttributionCorrectionCommand(
    Guid RequestId, string ResolutionNote, bool AttributionChanged, long ExpectedVersion);

/// <summary>
/// Attribution correction requests.
///
/// NOTHING HERE CHANGES A DONATION. That is the whole design: re-attributing a gift moves money
/// between campaigns in every report that follows it, and a fundraiser who believes a gift is
/// mis-credited is not, on their own, grounds for restating a campaign's income. What this records
/// is that somebody with a reason has raised it - which is enough to stop three people
/// investigating the same donation and to give whoever owns the correction something to act on.
/// </summary>
public sealed class AttributionCommandHandler(
    IAttributionCorrectionRepository corrections,
    ICampaignRepository campaigns,
    IFinancialDirectory financial,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<OutcomeResponse>> HandleAsync(
        RequestAttributionCorrectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "Say why the attribution looks wrong, so it can be assessed."));
        }

        // The donation is resolved through the Organisation filter first, so a request against
        // another organisation's donation answers "not found" rather than creating a record about
        // a gift the caller cannot see.
        var donation = await financial.GetAttributedDonationAsync(
            currentUser.Scope.TenantId, request.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That donation was not found inside your scope."));
        }

        var existing = await corrections.GetOpenForDonationAsync(request.DonationId, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<OutcomeResponse>(Error.Duplicate(
                "Somebody has already asked for this donation's attribution to be checked. "
                + "Add to that request rather than raising a second one."));
        }

        // A PROPOSED CAMPAIGN MUST EXIST AND BE VISIBLE. Recording a request pointing at a campaign
        // the requester cannot see would produce a correction nobody could act on.
        if (request.ProposedCampaignId is { } proposed)
        {
            var campaign = await campaigns.GetByIdAsync(proposed, cancellationToken);

            if (campaign is null)
            {
                return Result.Failure<OutcomeResponse>(
                    Error.NotFound("The campaign you proposed was not found inside your scope."));
            }
        }

        var correction = new AttributionCorrectionRequest
        {
            DonationId = donation.DonationId,
            DonationReference = donation.Reference,
            CurrentCampaignId = donation.CampaignId,
            CurrentTrackingAssetId = donation.TrackingAssetId,
            ProposedCampaignId = request.ProposedCampaignId,
            ProposedTrackingAssetId = request.ProposedTrackingAssetId,
            Reason = request.Reason.Trim(),
            IsResolved = false,
            AttributionChanged = false
        };

        await corrections.AddAsync(correction, cancellationToken);

        await audit.WriteAsync(
            AttributionAuditActionCodes.CorrectionRequested,
            nameof(AttributionCorrectionRequest),
            correction.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new OutcomeResponse(
            correction.Id,
            "Open",
            correction.Version,
            $"The attribution of {donation.Reference} has been raised for checking. "
            + "The donation itself is unchanged.",
            PermittedActions(true));
    }

    /// <summary>
    /// Closes a request.
    ///
    /// "CHECKED AND CORRECT" IS A RESOLUTION. Most correction requests end that way, and recording
    /// it separately from an actual change is what lets somebody tell how often tracking is really
    /// getting it wrong - a question worth being able to answer before spending on more of it.
    /// </summary>
    public async Task<Result<OutcomeResponse>> HandleAsync(
        ResolveAttributionCorrectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ResolutionNote))
        {
            return Result.Failure<OutcomeResponse>(Error.Validation(
                "Say what was decided, so the person who raised it knows what happened."));
        }

        var correction = await corrections.GetByIdAsync(command.RequestId, cancellationToken);

        if (correction is null)
        {
            return Result.Failure<OutcomeResponse>(
                Error.NotFound("That correction request was not found."));
        }

        if (correction.Version != command.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        if (correction.IsResolved)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                "That correction request has already been closed."));
        }

        correction.IsResolved = true;
        correction.ResolvedByUserId = currentUser.UserId;
        correction.ResolvedAtUtc = clock.UtcNow;
        correction.ResolutionNote = command.ResolutionNote.Trim();
        correction.AttributionChanged = command.AttributionChanged;

        await audit.WriteAsync(
            AttributionAuditActionCodes.CorrectionRequested,
            nameof(AttributionCorrectionRequest),
            correction.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = command.AttributionChanged
            ? "Closed. The attribution was corrected."
            : "Closed. The attribution was checked and found to be correct.";

        return new OutcomeResponse(
            correction.Id, "Resolved", correction.Version, message, PermittedActions(false));
    }

    private IReadOnlyList<string> PermittedActions(bool hasOpenCorrection)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.AttributionView))
        {
            actions.Add("View");
        }

        if (!hasOpenCorrection && currentUser.HasPermission(PermissionCodes.AttributionRequestCorrection))
        {
            actions.Add("RequestCorrection");
        }

        return actions;
    }
}
