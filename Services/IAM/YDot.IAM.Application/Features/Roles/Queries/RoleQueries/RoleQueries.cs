using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Roles.DTOs;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.Roles.Queries.RoleQueries;

/// <summary>The role catalogue grid.</summary>
public sealed record SearchRolesQuery(RoleSearchFilter Filter);

/// <summary>One role in full.</summary>
public sealed record GetRoleQuery(Guid RoleId);

/// <summary>Options for a role picker.</summary>
public sealed record LookupRolesQuery;

/// <summary>Who holds a role.</summary>
public sealed record GetRoleMembersQuery(Guid RoleId, PaginationRequest Pagination);

/// <summary>The permission matrix the role editor renders.</summary>
public sealed record GetPermissionMatrixQuery(Guid? RoleId);

/// <summary>The permission catalogue grid.</summary>
public sealed record SearchPermissionsQuery(PermissionSearchFilter Filter);

/// <summary>CSV export of the role catalogue.</summary>
public sealed record ExportRolesQuery(RoleSearchFilter Filter);

/// <summary>The read side of the Roles slice.</summary>
public sealed class RoleQueryHandler(
    IRoleReadService readService,
    IExportService exports,
    ITokenHasher tokenHasher,
    IAuditService audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ILogger<RoleQueryHandler> logger)
{
    public async Task<Result<PagedResponse<RoleListItemResponse>>> HandleAsync(
        SearchRolesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching roles.");

        var result = await readService.SearchAsync(query.Filter, cancellationToken);

        logger.LogInformation("Role search completed successfully. TotalCount {TotalCount}.", result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<RoleDetailResponse>> HandleAsync(
        GetRoleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving role detail. RoleId {RoleId}.", query.RoleId);

        var detail = await readService.GetDetailAsync(query.RoleId, cancellationToken);

        if (detail is null)
        {
            logger.LogWarning("Role detail could not be retrieved because RoleId {RoleId} was not found.", query.RoleId);
            return Result.Failure<RoleDetailResponse>(Error.NotFound("That role was not found."));
        }

        logger.LogInformation("Role detail retrieved successfully. RoleId {RoleId}.", query.RoleId);

        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<RoleLookupResponse>>> HandleAsync(
        LookupRolesQuery query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving role lookup options.");

        var roles = await readService.LookupAsync(cancellationToken);

        logger.LogInformation("Role lookup options retrieved successfully. Count {Count}.", roles.Count);

        return Result.Success(roles);
    }

    public async Task<Result<PagedResponse<RoleMemberResponse>>> HandleAsync(
        GetRoleMembersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Retrieving role members. RoleId {RoleId}, Page {Page}.", query.RoleId, query.Pagination.Page);

        var result = await readService.GetMembersAsync(query.RoleId, query.Pagination, cancellationToken);

        logger.LogInformation("Role members retrieved successfully. RoleId {RoleId}, TotalCount {TotalCount}.", query.RoleId, result.TotalCount);

        return Result.Success(result);
    }

    public async Task<Result<PermissionMatrixResponse>> HandleAsync(
        GetPermissionMatrixQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A ROLE belongs to one Organisation, so its grants are only read inside one. The
        // role-free catalogue is platform data and is answered at any scope - that is what the
        // SuperAdmin's Permission Catalogue screen reads.
        if (query.RoleId.HasValue && !tenantContext.HasTenant)
        {
            logger.LogWarning(
                "Permission matrix for a role requested without an organisation. RoleId {RoleId}.",
                query.RoleId);

            return Result.Failure<PermissionMatrixResponse>(Error.TenantSelectionRequired());
        }

        logger.LogInformation("Retrieving permission matrix. RoleId {RoleId}.", query.RoleId);

        var result = await readService.GetPermissionMatrixAsync(query.RoleId, cancellationToken);

        logger.LogInformation("Permission matrix retrieved successfully. RoleId {RoleId}.", query.RoleId);

        return Result.Success(result);
    }

    public async Task<Result<PagedResponse<PermissionListItemResponse>>> HandleAsync(
        SearchPermissionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Searching permissions.");

        var result = await readService.SearchPermissionsAsync(query.Filter, cancellationToken);

        logger.LogInformation("Permission search completed successfully. TotalCount {TotalCount}.", result.TotalCount);

        return Result.Success(result);
    }

    /// <summary>
    /// Exports the role catalogue.
    ///
    /// Audited, because a list of every role and its permission count is a map of how the
    /// Organisation access is structured.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportRolesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        logger.LogInformation("Starting role catalogue export.");

        var filter = query.Filter;
        filter.PageSize = 100;
        filter.Page = 1;

        var rows = new List<RoleExportRow>();
        PagedResponse<RoleListItemResponse> page;

        // Paged rather than one unbounded read, so a large catalogue does not arrive as a
        // single enormous query.
        do
        {
            page = await readService.SearchAsync(filter, cancellationToken);

            logger.LogInformation("Retrieved role export page {Page} of {TotalPages}. RowCount {RowCount}.", filter.Page, page.TotalPages, page.Items.Count);

            rows.AddRange(page.Items.Select(role => new RoleExportRow(
                role.Code,
                role.Name,
                role.Description,
                role.RoleType.ToString(),
                role.StatusDisplay,
                role.IsSystemRole ? "Yes" : "No",
                role.IsPrivileged ? "Yes" : "No",
                role.GrantsAllTenantPermissions ? "Yes" : "No",
                role.PermissionCount,
                role.MemberCount)));

            filter.Page++;
        }
        while (filter.Page <= page.TotalPages && filter.Page <= 100);

        var reference = tokenHasher.GenerateReference("EXP");
        var file = exports.ToCsv(rows, "roles", reference);

        await audit.WriteAsync(
            AuditActionCodes.RoleUpdated, nameof(Role), null, null,
            new { Action = "Exported", RowCount = rows.Count, Reference = reference },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Role catalogue export completed successfully. RowCount {RowCount}.", rows.Count);

        return Result.Success(file);
    }
}

/// <summary>One row of a role-catalogue export.</summary>
public sealed record RoleExportRow(
    string Code,
    string Name,
    string? Description,
    string RoleType,
    string Status,
    string IsSystemRole,
    string IsPrivileged,
    string GrantsAllPermissions,
    int PermissionCount,
    int MemberCount);
