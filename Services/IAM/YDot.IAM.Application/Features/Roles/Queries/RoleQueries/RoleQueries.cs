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
    IUnitOfWork unitOfWork)
{
    public async Task<Result<PagedResponse<RoleListItemResponse>>> HandleAsync(
        SearchRolesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchAsync(query.Filter, cancellationToken));
    }

    public async Task<Result<RoleDetailResponse>> HandleAsync(
        GetRoleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var detail = await readService.GetDetailAsync(query.RoleId, cancellationToken);

        return detail is null
            ? Result.Failure<RoleDetailResponse>(Error.NotFound("That role was not found."))
            : Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<RoleLookupResponse>>> HandleAsync(
        LookupRolesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await readService.LookupAsync(cancellationToken));

    public async Task<Result<PagedResponse<RoleMemberResponse>>> HandleAsync(
        GetRoleMembersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(
            await readService.GetMembersAsync(query.RoleId, query.Pagination, cancellationToken));
    }

    public async Task<Result<PermissionMatrixResponse>> HandleAsync(
        GetPermissionMatrixQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.GetPermissionMatrixAsync(query.RoleId, cancellationToken));
    }

    public async Task<Result<PagedResponse<PermissionListItemResponse>>> HandleAsync(
        SearchPermissionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await readService.SearchPermissionsAsync(query.Filter, cancellationToken));
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
