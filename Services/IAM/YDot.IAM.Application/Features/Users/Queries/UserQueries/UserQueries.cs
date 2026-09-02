using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Users.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.Queries.UserQueries;

/// <summary>The user directory grid.</summary>
public sealed record SearchUsersQuery(UserSearchFilter Filter);

/// <summary>One user in full.</summary>
public sealed record GetUserDetailQuery(Guid UserId);

/// <summary>Type-ahead for user pickers.</summary>
public sealed record LookupUsersQuery(string? Search, int Take = 20);

/// <summary>IAM-USR-04, sessions and devices.</summary>
/// <summary>
/// The Organisation's people, for a picker. Readable by every member.
/// </summary>
public sealed record GetPeopleDirectoryQuery(string? Search, int Take = 200);

public sealed record GetUserSecurityQuery(Guid UserId);

/// <summary>IAM-USR-03, effective access.</summary>
public sealed record GetUserAccessPreviewQuery(Guid UserId);

/// <summary>Summary tiles above the directory.</summary>
public sealed record GetUserStatisticsQuery;

/// <summary>CSV export of the directory.</summary>
public sealed record ExportUsersQuery(UserSearchFilter Filter);

/// <summary>The caller own profile.</summary>
public sealed record GetMyProfileQuery;

/// <summary>
/// The read side of the Users slice.
///
/// EVERY QUERY IS ALREADY ORGANISATION-SCOPED before it reaches here — the DbContext query
/// filter sees to that. What this handler adds is the two things a filter cannot express:
///
///   1. The CALLER data scope, passed down so a user with a narrowing scope sees only their
///      own records rather than the whole Organisation.
///   2. The sensitive-contact permission, which decides whether e-mail and mobile leave the
///      server in the clear or masked. Masking in the read service rather than the controller
///      means an unmasked value is never materialised where it could be logged.
/// </summary>
public sealed class UserQueryHandler(
    IUserReadService readService,
    IExportService exports,
    ITokenHasher tokenHasher,
    IAuditService audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<PagedResponse<UserListItemResponse>>> HandleAsync(
        SearchUsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await readService.SearchAsync(query.Filter, currentUser.Scope, cancellationToken);

        return Result.Success(page);
    }

    public async Task<Result<UserDetailResponse>> HandleAsync(
        GetUserDetailQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Somebody may always see their own contact details in full, whatever permissions
        // they hold. Masking a person own e-mail from them would be absurd.
        var canSeeContact = currentUser.HasPermission(PermissionCodes.UsersViewSensitiveContact)
                            || query.UserId == currentUser.UserId;

        var detail = await readService.GetDetailAsync(
            query.UserId, currentUser.Scope, canSeeContact, cancellationToken);

        if (detail is null)
        {
            return Result.Failure<UserDetailResponse>(Error.UserNotFound());
        }

        // Reading an unmasked contact detail is itself an auditable act, because it is the
        // moment personal data actually leaves the system.
        if (canSeeContact && query.UserId != currentUser.UserId)
        {
            await audit.WriteAsync(
                AuditActionCodes.UserUpdated, nameof(User), query.UserId, detail.DisplayName,
                new { Action = "ViewedSensitiveContact" }, cancellationToken: cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(detail);
    }

    public async Task<Result<IReadOnlyList<UserLookupResponse>>> HandleAsync(
        LookupUsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = Math.Clamp(query.Take, 1, 50);
        var results = await readService.LookupAsync(query.Search, take, cancellationToken);

        return Result.Success(results);
    }

    /// <summary>
    /// Who is in this Organisation, for the controls that ask a person to name a colleague.
    ///
    /// NO PERMISSION BEYOND BEING A MEMBER. Every "choose an owner" control needs this, and the
    /// people who use them - a Campaign Owner naming an owner, a Campaign Manager routing a lead
    /// - are not user administrators and never will be. Requiring iam.users.view meant those
    /// controls were empty for exactly the roles that use them.
    ///
    /// ACTIVE PEOPLE ONLY. Offering somebody suspended or withdrawn means work handed to an
    /// account that cannot sign in to do it, and nobody notices until the donor does.
    /// </summary>
    public async Task<Result<IReadOnlyList<PersonLookupResponse>>> HandleAsync(
        GetPeopleDirectoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = Math.Clamp(query.Take, 1, 500);

        var filter = new UserSearchFilter
        {
            Search = query.Search,
            Status = UserStatus.Active,
            Page = 1,
            PageSize = take,
            Sort = "displayName"
        };

        // ORGANISATION-WIDE, not the caller's own record scope. A Campaign Owner whose data scope
        // is their own campaigns still has to be able to name any colleague as an owner - the
        // scope governs which RECORDS they may see, not who exists to be chosen. The Organisation
        // boundary is still absolute: it comes from the context's query filter, and this widens
        // nothing beyond it.
        var scope = currentUser.Scope with { Scope = AccessScopeType.Tenant };

        var page = await readService.SearchAsync(filter, scope, cancellationToken);

        // The Organisation filter is the query filter on the context, not something written here:
        // a member can only ever see their own Organisation's people.
        // THE ROLE AND THE UNIT COME BACK TOO. A picker's second line is what tells two colleagues
        // with the same name apart, and without these the clients had nothing to put there but the
        // person's own code - which they then displayed under a heading reading "Role & region".
        // The list projection already carries both, so this costs no extra query.
        return Result.Success<IReadOnlyList<PersonLookupResponse>>(
        [
            .. page.Items.Select(user => new PersonLookupResponse(
                user.Id,
                user.DisplayName,
                user.Code,
                user.RoleNames.Count > 0 ? string.Join(", ", user.RoleNames) : null,
                user.OrganisationUnitName ?? user.DepartmentName))
        ]);
    }

    public async Task<Result<UserSecurityResponse>> HandleAsync(
        GetUserSecurityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A person may always see their own security page. Seeing somebody else needs the
        // permission — re-checked here rather than trusted from the route.
        if (query.UserId != currentUser.UserId
            && !currentUser.HasPermission(PermissionCodes.UserSecurityView))
        {
            return Result.Failure<UserSecurityResponse>(Error.Forbidden());
        }

        var security = await readService.GetSecurityAsync(query.UserId, cancellationToken);

        return security is null
            ? Result.Failure<UserSecurityResponse>(Error.UserNotFound())
            : Result.Success(security);
    }

    public async Task<Result<UserAccessPreviewResponse>> HandleAsync(
        GetUserAccessPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId != currentUser.UserId
            && !currentUser.HasPermission(PermissionCodes.PermissionsView))
        {
            return Result.Failure<UserAccessPreviewResponse>(Error.Forbidden());
        }

        var preview = await readService.GetAccessPreviewAsync(query.UserId, cancellationToken);

        return preview is null
            ? Result.Failure<UserAccessPreviewResponse>(Error.UserNotFound())
            : Result.Success(preview);
    }

    public async Task<Result<UserStatisticsResponse>> HandleAsync(
        GetUserStatisticsQuery query, CancellationToken cancellationToken)
    {
        var statistics = await readService.GetStatisticsAsync(currentUser.Scope, cancellationToken);

        return Result.Success(statistics);
    }

    public async Task<Result<UserDetailResponse>> HandleAsync(
        GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        var detail = await readService.GetDetailAsync(
            currentUser.UserId, currentUser.Scope, canSeeSensitiveContact: true, cancellationToken);

        return detail is null
            ? Result.Failure<UserDetailResponse>(Error.Unauthorised())
            : Result.Success(detail);
    }

    /// <summary>
    /// CSV export.
    ///
    /// Always audited, and always with a reference that also travels back on a response
    /// header. A spreadsheet of an Organisation entire staff list found on somebody desktop
    /// months later can then be traced to who exported it and when.
    /// </summary>
    public async Task<Result<ExportFile>> HandleAsync(
        ExportUsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var canSeeContact = currentUser.HasPermission(PermissionCodes.UsersViewSensitiveContact);

        var rows = await readService.GetExportRowsAsync(
            query.Filter, currentUser.Scope, canSeeContact, cancellationToken);

        var reference = tokenHasher.GenerateReference("EXP");
        var file = exports.ToCsv(rows, "users", reference);

        await audit.WriteAsync(
            AuditActionCodes.UserExported, nameof(User), null, null,
            new { RowCount = rows.Count, Reference = reference, UnmaskedContact = canSeeContact },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(file);
    }
}
