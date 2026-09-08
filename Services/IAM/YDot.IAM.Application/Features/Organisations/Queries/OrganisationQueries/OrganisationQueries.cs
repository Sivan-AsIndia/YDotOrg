using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.Organisations.Queries.OrganisationQueries;

/// <summary>The SuperAdmin Organisation directory.</summary>
public sealed record SearchOrganisationsQuery(TenantSearchFilter Filter);

/// <summary>One Organisation in full, by id. Platform scope.</summary>
public sealed record GetOrganisationQuery(Guid TenantId);

/// <summary>The caller own Organisation, resolved from the request context.</summary>
public sealed record GetMyOrganisationQuery;

/// <summary>Counts for the SuperAdmin dashboard tiles.</summary>
public sealed record GetOrganisationStatisticsQuery;

/// <summary>Everything waiting on SuperAdmin desk.</summary>
public sealed record GetOrganisationsAwaitingReviewQuery;

/// <summary>The BusinessUnit as the platform settings screen shows it.</summary>
public sealed record GetBusinessUnitQuery;

/// <summary>
/// The read side of the Organisation slice.
///
/// TWO DIFFERENT AUDIENCES, and the split is deliberate. The directory and the by-id detail
/// are PLATFORM reads — SuperAdmin looking across every Organisation, gated by the
/// <c>platform.organisations.*</c> permissions. <see cref="GetMyOrganisationQuery"/> is the
/// TENANT read: it takes no id at all, resolving the Organisation from the request context,
/// so a TenantAdmin has nothing to change in the URL to see somebody else.
/// </summary>
public sealed class OrganisationQueryHandler(
    IOrganisationReadService readService,
    IBusinessUnitRepository businessUnits,
    ITenantRepository tenants,
    IUserRepository users,
    ITenantContext tenantContext,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ILogger<OrganisationQueryHandler> logger)
{
    public async Task<Result<PagedResponse<OrganisationListItemResponse>>> HandleAsync(
        SearchOrganisationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching organisations.");

        // Defaulted rather than trusted from the query string: a caller naming a different
        // BusinessUnit would be reaching outside their own platform.
        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);

        var filter = query.Filter;
        filter.BusinessUnitId ??= businessUnit?.Id;

        var page = await readService.SearchAsync(filter, cancellationToken);

        logger.LogInformation("Organisation search completed successfully.");

        return Result.Success(page);
    }

    public async Task<Result<OrganisationDetailResponse>> HandleAsync(
        GetOrganisationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving organisation detail for TenantId {TenantId}.", query.TenantId);

        var detail = await readService.GetDetailAsync(query.TenantId, cancellationToken);

        if (detail is null)
        {
            logger.LogWarning("Organisation detail not found for TenantId {TenantId}.", query.TenantId);
            return Result.Failure<OrganisationDetailResponse>(Error.TenantNotFound());
        }

        logger.LogInformation("Organisation detail retrieved successfully for TenantId {TenantId}.", query.TenantId);

        return Result.Success(detail);
    }

    public async Task<Result<OrganisationDetailResponse>> HandleAsync(
        GetMyOrganisationQuery query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving current organisation.");

        if (!tenantContext.HasTenant)
        {
            logger.LogWarning("Current organisation cannot be retrieved because tenant selection is required.");
            return Result.Failure<OrganisationDetailResponse>(Error.TenantSelectionRequired());
        }

        var detail = await readService.GetCurrentAsync(cancellationToken);

        if (detail is null)
        {
            logger.LogWarning("Current organisation was not found.");
            return Result.Failure<OrganisationDetailResponse>(Error.TenantNotFound());
        }

        logger.LogInformation("Current organisation retrieved successfully.");

        return Result.Success(detail);
    }

    public async Task<Result<OrganisationStatisticsResponse>> HandleAsync(
        GetOrganisationStatisticsQuery query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving organisation statistics.");

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);

        if (businessUnit is null)
        {
            logger.LogError("Organisation statistics cannot be retrieved because the platform business unit is not configured.");
            return Result.Failure<OrganisationStatisticsResponse>(
                Error.Dependency("The platform is not configured."));
        }

        var statistics = await readService.GetStatisticsAsync(businessUnit.Id, cancellationToken);

        logger.LogInformation("Organisation statistics retrieved successfully.");

        return Result.Success(statistics);
    }

    public async Task<Result<IReadOnlyList<OrganisationListItemResponse>>> HandleAsync(
        GetOrganisationsAwaitingReviewQuery query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving organisations awaiting review.");

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);

        if (businessUnit is null)
        {
            logger.LogWarning("Organisations awaiting review could not be retrieved because the platform business unit is not configured.");
            return Result.Success<IReadOnlyList<OrganisationListItemResponse>>([]);
        }

        var awaiting = await readService.GetAwaitingReviewAsync(businessUnit.Id, cancellationToken);

        logger.LogInformation("Organisations awaiting review retrieved successfully. Count {Count}.", awaiting.Count);

        return Result.Success(awaiting);
    }

    public async Task<Result<BusinessUnitResponse>> HandleAsync(
        GetBusinessUnitQuery query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving platform business unit.");

        var businessUnit = await businessUnits.GetDefaultAsync(cancellationToken);

        if (businessUnit is null)
        {
            logger.LogError("Platform business unit could not be retrieved because the platform is not configured.");
            return Result.Failure<BusinessUnitResponse>(Error.Dependency("The platform is not configured."));
        }

        var tenantCount = await tenants.CountAsync(businessUnit.Id, cancellationToken);

        logger.LogInformation("Platform business unit retrieved successfully. TenantCount {TenantCount}.", tenantCount);

        return Result.Success(businessUnit.ToResponse(tenantCount));
    }

    /// <summary>
    /// The Organisation documents, for the review screen.
    ///
    /// Reading somebody paperwork is an auditable act, because these are the identity and
    /// registration documents of a real organisation.
    /// </summary>
    public async Task<Result<IReadOnlyList<OrganisationDocumentResponse>>> GetDocumentsAsync(
        Guid tenantId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving organisation documents for TenantId {TenantId}.", tenantId);

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            logger.LogWarning("Organisation documents could not be retrieved because TenantId {TenantId} was not found.", tenantId);
            return Result.Failure<IReadOnlyList<OrganisationDocumentResponse>>(Error.TenantNotFound());
        }

        var documents = await tenants.GetDocumentsAsync(tenantId, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.TenantDocumentReviewed, nameof(Tenant), tenantId, tenant.Name,
            new { Action = "ViewedDocuments", Count = documents.Count },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organisation documents retrieved successfully for TenantId {TenantId}. Count {Count}.", tenantId, documents.Count);

        return Result.Success<IReadOnlyList<OrganisationDocumentResponse>>(
            [.. documents.Select(document => document.ToDocumentResponse(asOf))]);
    }

    /// <summary>The hosts that reach an Organisation.</summary>
    public async Task<Result<IReadOnlyList<OrganisationDomainResponse>>> GetDomainsAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving organisation domains for TenantId {TenantId}.", tenantId);

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            logger.LogWarning("Organisation domains could not be retrieved because TenantId {TenantId} was not found.", tenantId);
            return Result.Failure<IReadOnlyList<OrganisationDomainResponse>>(Error.TenantNotFound());
        }

        var domains = await tenants.GetDomainsAsync(tenantId, cancellationToken);

        logger.LogInformation("Organisation domains retrieved successfully for TenantId {TenantId}. Count {Count}.", tenantId, domains.Count);

        return Result.Success<IReadOnlyList<OrganisationDomainResponse>>(
            [.. domains.Select(OrganisationMappingConfig.ToDomainResponse)]);
    }

    /// <summary>
    /// The Organisation lifecycle timeline, for the PLATFORM reviewer.
    ///
    /// THE INTERNAL NOTES ARE INCLUDED HERE and only here. The endpoint behind this carries
    /// <c>platform.tenants.view</c>, which is the reviewer's own permission; the tenant's view of
    /// its own timeline comes through <c>GetMyOrganisationQuery</c>, which withholds them.
    /// </summary>
    public async Task<Result<IReadOnlyList<OrganisationTimelineResponse>>> GetTimelineAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving organisation timeline for TenantId {TenantId}.", tenantId);

        var history = await tenants.GetStatusHistoryAsync(tenantId, cancellationToken);

        logger.LogInformation("Organisation timeline retrieved successfully for TenantId {TenantId}. Count {Count}.", tenantId, history.Count);

        return Result.Success<IReadOnlyList<OrganisationTimelineResponse>>(
            [.. history.Select(item => item.ToTimelineResponse(includeInternalNotes: true))]);
    }

    /// <summary>How many users an Organisation has, for the licence display.</summary>
    public async Task<Result<int>> GetUserCountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving user count for TenantId {TenantId}.", tenantId);

        var count = await users.CountForTenantAsync(tenantId, cancellationToken);

        logger.LogInformation("User count retrieved successfully for TenantId {TenantId}. Count {Count}.", tenantId, count);

        return Result.Success(count);
    }
}
