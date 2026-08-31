using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A department inside one Organisation. Tenant-owned master data, so two Organisations may
/// both have a "Finance" department with no relationship between them.
/// </summary>
public class Department : TenantEntity, ICodedEntity
{
    /// <summary>Unique inside the Tenant, for example FIN.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Departments nest, so a division can contain teams.</summary>
    public Guid? ParentDepartmentId { get; set; }

    public Department? Parent { get; set; }

    public ICollection<Department> Children { get; set; } = [];

    /// <summary>The user who heads it. Must belong to the same Organisation.</summary>
    public Guid? HeadUserId { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public int DisplayOrder { get; set; }

    public ICollection<User> Users { get; set; } = [];

    public bool IsAssignable => Status == RecordStatus.Active;
}
