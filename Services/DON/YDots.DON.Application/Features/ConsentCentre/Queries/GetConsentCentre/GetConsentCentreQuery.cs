using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.ConsentCentre.DTOs;
using YDots.DON.Application.Features.ConsentCentre.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.ConsentCentre.Queries.GetConsentCentre;

/// <summary>SCR-DON-005 GET. Record notices, permissions, opt-outs and public-recognition preference.</summary>
public sealed record GetConsentCentreQuery(ConsentSearchFilter Filter);

/// <summary>SCR-DON-005 Review evidence. Reading a confidential evidence reference is audited.</summary>
public sealed record GetConsentEvidenceQuery(Guid ConsentId);

public sealed class ConsentCentreQueryHandler(
    IConsentRepository consentRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<ConsentCentreResponse>> HandleAsync(
        GetConsentCentreQuery query,
        CancellationToken cancellationToken = default)
    {
        var canSeeEvidence = currentUser.CanSeeEvidence();

        var page = await consentRepository.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);
        var rows = page.Items.Select(consent => consent.ToListItemResponse(canSeeEvidence)).ToList();

        // The history panel only makes sense for a single donor, so it is loaded on demand
        // rather than for the whole queue.
        var history = query.Filter.DonorId is null
            ? []
            : (await consentRepository.GetHistoryAsync(query.Filter.DonorId.Value, cancellationToken))
                .Select(consent => consent.ToListItemResponse(canSeeEvidence))
                .ToList();

        var response = new ConsentCentreResponse(
            ScreenIds.ConsentAndPreferenceCentre,
            ScreenRoutes.ConsentAndPreferenceCentre,
            new PagedResponse<ConsentListItemResponse>(rows, page.TotalCount, page.Page, page.PageSize),
            history,
            ToLookup<ConsentChannel>(),
            ToLookup<ConsentState>(),
            ToLookup<ConsentStatus>(),
            _settings.CurrentNoticeVersion,
            BuildPermittedActions(),
            DescribeFilter(query.Filter),
            DescribeScope(),
            rows.Count == 0 ? ScreenState.Empty : ScreenState.Initial);

        return Result.Success(response);
    }

    public async Task<Result<ConsentListItemResponse>> HandleAsync(
        GetConsentEvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        var consent = await consentRepository.GetByIdAsync(query.ConsentId, cancellationToken);

        if (consent is null || consent.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<ConsentListItemResponse>(
                Error.NotFound("That consent record was not found inside your scope."));
        }

        var canSeeEvidence = currentUser.CanSeeEvidence();

        if (canSeeEvidence)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(AuditActionCodes.ConsentEvidenceViewed, nameof(Consent), consent.Id,
                    AuditResult.Succeeded, $"Evidence for {consent.Channel} consent reviewed."),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(consent.ToListItemResponse(canSeeEvidence));
    }

    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string> { "Review evidence" };

        if (currentUser.HasPermission(PermissionCodes.ConsentCentreGrant))
        {
            actions.Insert(0, "Grant");
        }

        if (currentUser.HasPermission(PermissionCodes.ConsentCentreWithdraw))
        {
            actions.Add("Withdraw");
        }

        if (currentUser.HasPermission(PermissionCodes.ConsentCentreCorrect))
        {
            actions.Add("Correct");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    private static string DescribeFilter(ConsentSearchFilter filter)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            parts.Add($"search '{filter.Search}'");
        }

        if (filter.DonorId is not null)
        {
            parts.Add("donor filter");
        }

        if (filter.Channel is not null)
        {
            parts.Add($"channel {filter.Channel}");
        }

        if (filter.ConsentState is not null)
        {
            parts.Add($"state {filter.ConsentState}");
        }

        if (filter.Status is not null)
        {
            parts.Add($"status {filter.Status}");
        }

        if (!string.IsNullOrWhiteSpace(filter.NoticeVersion))
        {
            parts.Add($"notice {filter.NoticeVersion}");
        }

        if (filter.IncludeHistory)
        {
            parts.Add("including superseded rows");
        }

        return parts.Count == 0 ? "No filters applied." : "Filtered by " + string.Join(", ", parts) + ".";
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
