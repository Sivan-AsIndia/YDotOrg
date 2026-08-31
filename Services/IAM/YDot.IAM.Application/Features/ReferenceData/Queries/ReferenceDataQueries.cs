using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.ReferenceData.Queries;

/// <summary>Everything the create and edit screens need to render their dropdowns, in one call.</summary>
public sealed record GetReferenceDataQuery;

/// <summary>The enum values the client needs, so it never hard-codes a list that can drift.</summary>
public sealed record GetEnumOptionsQuery;

/// <summary>
/// The reference data a screen needs before it can be drawn.
///
/// RETURNED AS ONE PAYLOAD, DELIBERATELY. A user-create form needs roles, departments, units
/// and managers. Four endpoints would mean four round trips before the form is usable, and
/// four chances for one of them to fail and leave the screen half-populated.
///
/// EVERYTHING IS ALREADY ORGANISATION-SCOPED by the query filter, so a department from another
/// Organisation cannot appear in the list - and therefore cannot be selected.
/// </summary>
public sealed class ReferenceDataQueryHandler(
    ILookupRepository lookups,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
{
    public async Task<Result<ReferenceDataResponse>> HandleAsync(
        GetReferenceDataQuery query, CancellationToken cancellationToken)
    {
        // FETCHED ONE AFTER ANOTHER, NOT IN PARALLEL. These six reads share one DbContext, and
        // EF Core permits exactly one operation on a context at a time - starting them together
        // throws "a second operation was started on this context instance" rather than running
        // them concurrently. Nor was there anything to win: one context means one connection,
        // so the database would have serialised them regardless.
        var roles = await lookups.GetRolesAsync(cancellationToken);
        var departments = await lookups.GetDepartmentsAsync(cancellationToken);
        var units = await lookups.GetOrganisationUnitsAsync(cancellationToken);
        var managers = await lookups.GetManagersAsync(cancellationToken);
        var permissions = await lookups.GetPermissionsAsync(cancellationToken);
        var tenants = await lookups.GetSelectableTenantsAsync(cancellationToken);

        return Result.Success(new ReferenceDataResponse(
            roles,
            departments,
            units,
            managers,
            permissions,
            // Empty for a Tenant user. The switcher is not part of their interface.
            tenants,
            BuildEnumOptions(),
            tenantContext.TenantId,
            tenantContext.TenantName,
            currentUser.IsSuperAdmin,
            currentUser.IsTenantAdmin));
    }

    public Task<Result<EnumOptionsResponse>> HandleAsync(
        GetEnumOptionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(BuildEnumOptions()));

    /// <summary>
    /// The enum values, served from the server.
    ///
    /// The client could hard-code these, and then they would drift the first time a value is
    /// added. Serving them means one source of truth and a dropdown that is never stale.
    /// </summary>
    private static EnumOptionsResponse BuildEnumOptions() => new(
        Describe<UserStatus>(),
        Describe<UserAccountCategory>(),
        Describe<EngagementType>(),
        Describe<MfaRequirement>(),
        Describe<MfaMethodType>(),
        Describe<RoleStatus>(),
        Describe<RoleType>(),
        Describe<PermissionAction>(),
        Describe<DataScopeType>(),
        Describe<TenantStatus>(),
        Describe<TenantDocumentType>(),
        Describe<AccessRequestType>(),
        Describe<AccessRequestStatus>(),
        Describe<AccessReviewStatus>(),
        Describe<AccessReviewDecision>(),
        Describe<BulkActionType>(),
        Describe<ClientType>(),
        Describe<PrivilegeLevel>());

    /// <summary>Turns an enum into name/label pairs, with the label humanised for display.</summary>
    private static IReadOnlyList<EnumOption> Describe<TEnum>() where TEnum : struct, Enum =>
    [
        .. Enum.GetValues<TEnum>()
            .Select(value => new EnumOption(
                value.ToString(),
                Humanise(value.ToString()!),
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)))
    ];

    /// <summary>"ProfileIncomplete" becomes "Profile incomplete".</summary>
    private static string Humanise(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);
        builder.Append(value[0]);

        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(value[index]));
            }
            else
            {
                builder.Append(value[index]);
            }
        }

        return builder.ToString();
    }
}

/// <summary>Everything a create or edit screen needs, in one payload.</summary>
public sealed record ReferenceDataResponse(
    IReadOnlyList<LookupItem> Roles,
    IReadOnlyList<LookupItem> Departments,
    IReadOnlyList<LookupItem> OrganisationUnits,
    IReadOnlyList<LookupItem> Managers,
    IReadOnlyList<LookupItem> Permissions,
    IReadOnlyList<LookupItem> SelectableOrganisations,
    EnumOptionsResponse Enums,
    Guid? CurrentTenantId,
    string? CurrentTenantName,
    bool IsSuperAdmin,
    bool IsTenantAdmin);

/// <summary>One selectable enum value.</summary>
public sealed record EnumOption(string Value, string Label, int Ordinal);

/// <summary>Every enum the client renders as a dropdown.</summary>
public sealed record EnumOptionsResponse(
    IReadOnlyList<EnumOption> UserStatuses,
    IReadOnlyList<EnumOption> AccountCategories,
    IReadOnlyList<EnumOption> EngagementTypes,
    IReadOnlyList<EnumOption> MfaRequirements,
    IReadOnlyList<EnumOption> MfaMethodTypes,
    IReadOnlyList<EnumOption> RoleStatuses,
    IReadOnlyList<EnumOption> RoleTypes,
    IReadOnlyList<EnumOption> PermissionActions,
    IReadOnlyList<EnumOption> DataScopeTypes,
    IReadOnlyList<EnumOption> OrganisationStatuses,
    IReadOnlyList<EnumOption> DocumentTypes,
    IReadOnlyList<EnumOption> AccessRequestTypes,
    IReadOnlyList<EnumOption> AccessRequestStatuses,
    IReadOnlyList<EnumOption> AccessReviewStatuses,
    IReadOnlyList<EnumOption> AccessReviewDecisions,
    IReadOnlyList<EnumOption> BulkActionTypes,
    IReadOnlyList<EnumOption> ClientTypes,
    IReadOnlyList<EnumOption> PrivilegeLevels);
