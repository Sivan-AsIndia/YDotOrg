using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.IdentityVerification.DTOs;
using YDots.DON.Application.Features.IdentityVerification.Mappings;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Features.IdentityVerification.Queries.GetVerifications;

/// <summary>DON-UI-07 GET list.</summary>
public sealed record GetVerificationListQuery(VerificationSearchFilter Filter);

/// <summary>DON-UI-07 GET one.</summary>
public sealed record GetVerificationDetailQuery(Guid VerificationId);

public sealed class IdentityVerificationQueryHandler(
    IVerificationRepository verificationRepository,
    ICurrentUser currentUser,
    IOptions<DonorSettings> donorSettings)
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public async Task<Result<IdentityVerificationListResponse>> HandleAsync(
        GetVerificationListQuery query,
        CancellationToken cancellationToken = default)
    {
        var canSeeEvidence = currentUser.CanSeeEvidence();
        var page = await verificationRepository.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);

        var rows = page.Items
            .Select(item => item.ToResponse(canSeeEvidence, _settings.VerificationMaxAttempts))
            .ToList();

        var response = new IdentityVerificationListResponse(
            ScreenIds.DonorIdentityVerification,
            ScreenRoutes.DonorIdentityVerification,
            new PagedResponse<IdentityVerificationResponse>(rows, page.TotalCount, page.Page, page.PageSize),
            ToLookup<VerificationChannel>(),
            ToLookup<VerificationStatus>(),
            ToLookup<IdentityConfidence>(),
            BuildPermittedActions(),
            DescribeFilter(query.Filter),
            DescribeScope(),
            _settings.VerificationCodeValidMinutes,
            _settings.VerificationMaxAttempts,
            rows.Count == 0 ? ScreenState.Empty : ScreenState.Initial);

        return Result.Success(response);
    }

    public async Task<Result<IdentityVerificationResponse>> HandleAsync(
        GetVerificationDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var verification = await verificationRepository.GetByIdAsync(query.VerificationId, cancellationToken);

        if (verification is null || verification.OrganisationId != currentUser.OrganisationId)
        {
            return Result.Failure<IdentityVerificationResponse>(
                Error.NotFound("That verification was not found inside your scope."));
        }

        return Result.Success(verification.ToResponse(
            currentUser.CanSeeEvidence(), _settings.VerificationMaxAttempts));
    }

    private IReadOnlyList<string> BuildPermittedActions()
    {
        var actions = new List<string> { "View" };

        if (currentUser.HasPermission(PermissionCodes.VerificationSendChallenge))
        {
            actions.Insert(0, "Send challenge");
        }

        if (currentUser.HasPermission(PermissionCodes.VerificationVerifyCode))
        {
            actions.Add("Verify code");
        }

        if (currentUser.HasPermission(PermissionCodes.VerificationEscalateReview))
        {
            actions.Add("Escalate review");
        }

        if (currentUser.HasPermission(PermissionCodes.VerificationCancel))
        {
            actions.Add("Cancel verification");
        }

        return actions;
    }

    private string DescribeScope() =>
        currentUser.Scope.IsOwnRecordsOnly ? "Records assigned to you" : "Your whole organisation";

    private static string DescribeFilter(VerificationSearchFilter filter)
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

        if (filter.Status is not null)
        {
            parts.Add($"status {filter.Status}");
        }

        if (filter.Channel is not null)
        {
            parts.Add($"channel {filter.Channel}");
        }

        if (filter.IdentityConfidence is not null)
        {
            parts.Add($"confidence {filter.IdentityConfidence}");
        }

        if (filter.ReviewerUserId is not null)
        {
            parts.Add("reviewer filter");
        }

        return parts.Count == 0 ? "No filters applied." : "Filtered by " + string.Join(", ", parts) + ".";
    }

    private static IReadOnlyList<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new LookupItem(value.ToString(), value.ToString()))];
}
