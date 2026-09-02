using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.Features.LeadCapture.DTOs;
using YDots.DON.Application.Features.Leads.DTOs;
using YDots.DON.Application.Features.Leads.Mappings;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.LeadCapture.Queries.GetLeadCapture;

/// <summary>
/// SCR-DON-002 GET. Pass a lead id to edit an existing draft, or omit it to open a blank form.
/// </summary>
public sealed record GetLeadCaptureQuery(Guid? LeadId);

public sealed class GetLeadCaptureQueryHandler(
    ILeadRepository leadRepository,
    ICampaignRepository campaignRepository,
    IConsentRepository consentRepository,
    ICurrentUser currentUser,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<LeadCaptureResponse>> HandleAsync(
        GetLeadCaptureQuery query,
        CancellationToken cancellationToken = default)
    {
        LeadDetailResponse? lead = null;
        var duplicates = new List<DuplicateCandidateResponse>();

        if (query.LeadId is not null)
        {
            var entity = await leadRepository.GetByIdAsync(query.LeadId.Value, cancellationToken);

            if (entity is null || entity.OrganisationId != currentUser.OrganisationId)
            {
                return Result.Failure<LeadCaptureResponse>(Error.NotFound("That lead was not found inside your scope."));
            }

            if (currentUser.Scope.IsOwnRecordsOnly && entity.OwnerUserId != currentUser.UserId)
            {
                return Result.Failure<LeadCaptureResponse>(Error.NotFound("That lead was not found inside your scope."));
            }

            var consents = await consentRepository.GetForLeadAsync(entity.Id, cancellationToken);

            lead = entity.ToDetailResponse(currentUser.CanSeeContact(), currentUser.CanSeeEvidence(), consents);

            // The duplicate panel is read-only and always safe: category and route, never the
            // other person's details.
            if (!string.IsNullOrWhiteSpace(entity.DuplicateCandidateSummary))
            {
                duplicates.Add(new DuplicateCandidateResponse(
                    Guid.Empty,
                    "Lead",
                    "Previously detected",
                    IdentityConfidence.Medium.ToString(),
                    entity.DuplicateCandidateSummary,
                    ScreenRoutes.DuplicateReview));
            }
        }

        var campaigns = await campaignRepository.GetActiveAsync(currentUser.OrganisationId, cancellationToken);
        var owners = await leadRepository.GetKnownOwnersAsync(currentUser.OrganisationId, cancellationToken);

        var response = new LeadCaptureResponse(
            ScreenIds.LeadCapture,
            ScreenRoutes.LeadCapture,
            lead,
            [.. campaigns.Select(campaign => new LookupItem(campaign.Id.ToString(), campaign.Name, campaign.Code))],
            SupportedLanguages.All,
            ToLookup<ConsentChannel>(),
            ToLookup<ConsentState>(),
            [.. owners.Select(owner => new LookupItem(owner.UserId.ToString(), owner.Name, owner.TeamCode))],
            _settings.CurrentNoticeVersion,
            duplicates,
            BuildPermittedActions(lead),
            DescribeScope(),
            lead is null ? ScreenState.Initial : ScreenState.Success);

        return Result.Success(response);
    }

    /// <summary>
    /// Which buttons the screen may draw. Permission and record state both matter, but only
    /// where the action genuinely needs a record: Deduplicate has nothing to compare and Delete
    /// unused draft has nothing to remove until one exists.
    ///
    /// SUBMIT IS NOT ONE OF THOSE. It was withheld until <c>lead</c> was non-null, and on a blank
    /// form that is every first-time capture - so the one screen whose stated purpose is "capture
    /// a lead and send it to the Lead Queue" offered no way to send it, and the only caller who
    /// ever saw the button was someone re-opening a draft. The screen's Submit saves first and
    /// submits the record it just created, so the question this answers is whether the CALLER may
    /// submit, not whether a row happens to exist yet.
    ///
    /// It matters that this is answered honestly rather than inferred client-side: an APPROVER
    /// holds <c>save</c> (an Edit) and not <c>submit</c> (a Submit), so anything that treats Save
    /// as evidence of Submit draws them a button the endpoint refuses.
    /// </summary>
    private IReadOnlyList<string> BuildPermittedActions(LeadDetailResponse? lead)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.LeadCaptureSave))
        {
            actions.Add("Save");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadCaptureDeduplicate) && lead is not null)
        {
            actions.Add("Deduplicate");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadCaptureSubmit))
        {
            actions.Add("Submit");
        }

        if (currentUser.HasPermission(PermissionCodes.LeadCaptureDeleteDraft) && lead is { IsDraft: true })
        {
            actions.Add("Delete unused draft");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
