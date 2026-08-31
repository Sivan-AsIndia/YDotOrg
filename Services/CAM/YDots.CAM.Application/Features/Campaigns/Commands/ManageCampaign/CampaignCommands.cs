using Microsoft.Extensions.Logging;
using YDots.CAM.Application.Common.Abstractions.Persistence;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;
using YDots.CAM.Application.Common.Results;
using YDots.CAM.Application.Features.Campaigns.DTOs;
using YDots.CAM.Application.Features.Campaigns.Mappings;
using YDots.CAM.Domain.Entities;
using YDots.CAM.Domain.Enums;

namespace YDots.CAM.Application.Features.Campaigns.Commands.ManageCampaign;

/// <summary>Creates a campaign inside the caller's Organisation.</summary>
public sealed record CreateCampaignCommand(CreateCampaignRequest Request);

/// <summary>Edits a Draft campaign.</summary>
public sealed record UpdateCampaignCommand(Guid CampaignId, UpdateCampaignRequest Request);

/// <summary>Deletes a Draft campaign. Refused once anything hangs off it.</summary>
public sealed record DeleteDraftCampaignCommand(Guid CampaignId, CampaignLifecycleRequest Request);

/// <summary>
/// Campaign creation, editing and draft deletion.
///
/// WHAT REPLACED WHAT. This one class replaces CreateCampaignCommandHandler,
/// UpdateCampaignCommandHandler and DeleteDraftCampaignCommandHandler, each of which was a
/// separate MediatR <c>IRequestHandler</c> in its own folder. MediatR is gone: the controller
/// injects this handler and calls it directly, so the path from route to logic is one you can
/// follow by clicking rather than one the container assembles at runtime.
///
/// THREE THINGS EACH OF THOSE HANDLERS DID BY HAND ARE NOW AUTOMATIC, and every one of them
/// was a place to forget something:
///
///   - <c>entity.OrganisationId = currentUser.OrganisationId</c>. The DbContext stamps it from
///     the tenant context, so a handler cannot write a row owned by the wrong Organisation and
///     cannot write one owned by nobody.
///   - <c>CreatedAtUtc</c>, <c>UpdatedByUserId</c>, <c>Version++</c>. Stamped on save. The old
///     code incremented the version by hand in each handler, and any handler that forgot broke
///     concurrency detection silently.
///   - Validation by <c>throw new CustomValidationException</c>. FluentValidation runs in the
///     pipeline and a refusal is a Result, so a validation failure is no longer an exception
///     unwinding through middleware.
/// </summary>
public sealed class CampaignCommandHandler(
    ICampaignRepository campaigns,
    IAuditWriter audit,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<CampaignCommandHandler> logger)
{
    public async Task<Result<CampaignDetailResponse>> HandleAsync(
        CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        // A Tenant-owned write with no Organisation resolved would produce a row nobody can
        // ever read back. Refused up front rather than saved and lost.
        if (!tenantContext.HasTenant)
        {
            return Result.Failure<CampaignDetailResponse>(Error.TenantSelectionRequired());
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await campaigns.CodeExistsAsync(code, null, cancellationToken))
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.Duplicate($"A campaign with code {code} already exists in this organisation."));
        }

        var campaign = request.ToEntity();

        // A campaign created straight into Submitted still has to record WHO submitted it, or
        // the segregation-of-duties check on approval has nothing to compare against.
        if (campaign.Status == CampaignStatus.Submitted)
        {
            campaign.SubmittedByUserId = currentUser.UserId;
            campaign.SubmittedAtUtc = clock.UtcNow;
        }

        await campaigns.AddAsync(campaign, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.CampaignCreated, nameof(Campaign), campaign.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Campaign {CampaignId} created with code {CampaignCode} in organisation {TenantId}.",
            campaign.Id, campaign.Code, campaign.TenantId);

        return campaign.ToDetailResponse(
            pendingCloseRequest: null,
            permittedActions: PermittedActions(campaign, hasOutstandingChecks: false, hasPendingClose: false));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateCampaignCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);
        if (campaign is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That campaign was not found."));
        }

        if (campaign.Version != request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // ONLY A DRAFT MAY BE EDITED. Past that an approver has seen it, and changing the
        // target or the dates underneath them would make the approval meaningless.
        if (!campaign.IsDraft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Draft campaign can be edited. This one is {campaign.Status}."));
        }

        request.ApplyTo(campaign);

        await audit.WriteAsync(
            AuditActionCodes.CampaignUpdated, nameof(Campaign), campaign.Id,
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BuildOutcomeAsync(campaign, "Campaign updated.", cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteDraftCampaignCommand command, CancellationToken cancellationToken)
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

        if (!campaign.IsDraft)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"Only a Draft campaign can be deleted. This one is {campaign.Status}. "
                + "Close it instead."));
        }

        // A DRAFT CAN STILL HAVE TRACKING ASSETS, which is easy to miss. Deleting the campaign
        // would orphan them, and a tracking reference that has already been printed on a poster
        // outlives the draft that produced it.
        var trackingAssetCount = await campaigns.CountTrackingAssetsAsync(campaign.Id, cancellationToken);

        if (trackingAssetCount > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InUse(
                $"This campaign has {trackingAssetCount} tracking asset(s). Remove them first."));
        }

        var snapshot = new { campaign.Code, campaign.Name };

        campaigns.Delete(campaign);

        await audit.WriteAsync(
            AuditActionCodes.CampaignDraftDeleted, nameof(Campaign), campaign.Id,
            command.Request.DetailedReason, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Draft campaign {CampaignId} ({CampaignCode}) deleted.", campaign.Id, snapshot.Code);

        return new OutcomeResponse(
            campaign.Id, campaign.Status.ToString(), campaign.Version, "Draft campaign deleted.", []);
    }

    /// <summary>
    /// The answer a state-changing action returns, with the counts that decide which buttons
    /// the screen may draw next.
    ///
    /// The readiness count is re-read rather than assumed, because an edit can change which
    /// checks apply - and a toolbar offering Activate on a campaign with an outstanding
    /// required check is a button that exists only to answer 409.
    /// </summary>
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
            PermittedActions(campaign, outstanding.Count > 0, pendingClose is not null));
    }

    private IReadOnlyList<string> PermittedActions(
        Campaign campaign, bool hasOutstandingChecks, bool hasPendingClose) =>
        CampaignMappingConfig.PermittedActionsFor(
            campaign, currentUser.UserId, currentUser.HasPermission, hasOutstandingChecks, hasPendingClose);
}
