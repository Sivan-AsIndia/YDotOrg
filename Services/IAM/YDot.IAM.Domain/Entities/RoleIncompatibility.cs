using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A segregation-of-duties rule: these two roles may not be held by the same person.
///
/// The classic case is that whoever raises a payment must not also approve it. Encoding the
/// pair as data rather than as a hard-coded check means an Organisation can declare its own
/// conflicts, and the assignment screen can refuse the combination at the point somebody
/// tries to create it rather than discovering it at the next audit.
///
/// The rule is symmetric — A conflicts with B implies B conflicts with A — so it is stored
/// once and checked in both directions.
/// </summary>
public class RoleIncompatibility : TenantEntity
{
    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public Guid ConflictingRoleId { get; set; }

    public Role? ConflictingRole { get; set; }

    /// <summary>Shown to whoever hits the rule, so the refusal is explicable.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// When true the combination is refused outright. When false it is allowed but flagged
    /// for review, which is the right setting for a small Organisation where one person
    /// genuinely has to wear both hats.
    /// </summary>
    public bool IsBlocking { get; set; } = true;

    public bool IsActive { get; set; } = true;
}
