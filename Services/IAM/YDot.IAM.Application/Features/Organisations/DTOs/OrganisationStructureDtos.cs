using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Organisations.DTOs;

// =====================================================================================
// Departments and organisation units — the Organisation-owned structural masters.
//
// TWO SEPARATE HIERARCHIES, DELIBERATELY. A department is what somebody DOES (Fundraising,
// Finance); a unit is where they SIT (Head office, Southern region). Most organisations need
// both, and collapsing them into one tree forces a choice that then has to be undone: a
// fundraiser in the southern office belongs to Fundraising AND to Southern, and neither is a
// child of the other.
//
// Both are Tenant-owned, so every operation below is scoped to the caller's Organisation by the
// query filter. Neither takes an Organisation as a parameter.
// =====================================================================================

/// <summary>A department, with the counts a management screen needs.</summary>
public sealed record DepartmentResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    string? ParentName,
    Guid? HeadUserId,
    string? HeadDisplayName,
    RecordStatus Status,
    int DisplayOrder,

    /// <summary>
    /// How many people are in it. Shown because deleting a department that still has members is
    /// refused, and finding that out only when the button fails is a poor experience.
    /// </summary>
    int MemberCount,

    int ChildCount,
    long Version);

/// <summary>Creating a department.</summary>
public sealed record CreateDepartmentRequest(
    string Name,

    /// <summary>
    /// Unique inside the Organisation. Every master in this system carries one: a name changes
    /// when somebody reorganises, and anything that referenced it by name breaks quietly.
    /// </summary>
    string Code,

    string? Description = null,
    Guid? ParentDepartmentId = null,
    Guid? HeadUserId = null,
    int DisplayOrder = 0);

/// <summary>Editing a department. Every field is optional; only what is sent is changed.</summary>
public sealed record UpdateDepartmentRequest(
    long ExpectedVersion,
    string? Name = null,
    string? Code = null,
    string? Description = null,
    Guid? ParentDepartmentId = null,
    Guid? HeadUserId = null,
    RecordStatus? Status = null,
    int? DisplayOrder = null);

/// <summary>An organisation unit, with the counts a management screen needs.</summary>
public sealed record OrganisationUnitResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentUnitId,
    string? ParentName,
    string? UnitType,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? Country,
    string? PostalCode,
    string? ContactEmail,
    string? ContactPhone,
    string? TimeZone,
    Guid? ManagerUserId,
    string? ManagerDisplayName,
    RecordStatus Status,
    int DisplayOrder,
    int MemberCount,
    int ChildCount,
    long Version);

/// <summary>Creating an organisation unit.</summary>
public sealed record CreateOrganisationUnitRequest(
    string Name,
    string Code,
    string? Description = null,
    Guid? ParentUnitId = null,
    string? UnitType = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? PostalCode = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? TimeZone = null,
    Guid? ManagerUserId = null,
    int DisplayOrder = 0);

/// <summary>Editing an organisation unit. Every field is optional; only what is sent is changed.</summary>
public sealed record UpdateOrganisationUnitRequest(
    long ExpectedVersion,
    string? Name = null,
    string? Code = null,
    string? Description = null,
    Guid? ParentUnitId = null,
    string? UnitType = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? PostalCode = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? TimeZone = null,
    Guid? ManagerUserId = null,
    RecordStatus? Status = null,
    int? DisplayOrder = null);

/// <summary>Removing a department or a unit.</summary>
public sealed record DeleteStructureRequest(long ExpectedVersion, string? Reason = null);
