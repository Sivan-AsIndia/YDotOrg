using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.DTOs;

/// <summary>Filter for the permission catalogue.</summary>
public sealed class PermissionSearchFilter : PaginationRequest
{
    public string? ModuleCode { get; set; }

    public string? GroupCode { get; set; }

    public PermissionAction? Action { get; set; }

    public PermissionStatus? Status { get; set; }

    public bool? IsSensitive { get; set; }

    /// <summary>
    /// Excludes the platform-only codes. The role editor sets this, because those codes can
    /// never be attached to a Tenant role and offering them would be misleading.
    /// </summary>
    public bool? TenantAssignableOnly { get; set; }
}
