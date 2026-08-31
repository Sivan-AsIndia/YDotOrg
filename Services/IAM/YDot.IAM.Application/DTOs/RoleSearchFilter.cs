using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.DTOs;

/// <summary>Filter for the role catalogue grid.</summary>
public sealed class RoleSearchFilter : PaginationRequest
{
    public RoleStatus? Status { get; set; }

    public RoleType? RoleType { get; set; }

    public bool? IsSystemRole { get; set; }

    public bool? IsPrivileged { get; set; }

    /// <summary>Roles carrying this permission. Answers "who can approve payments?".</summary>
    public string? PermissionCode { get; set; }
}
