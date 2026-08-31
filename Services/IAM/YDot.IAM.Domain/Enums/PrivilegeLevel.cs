namespace YDot.IAM.Domain.Enums;

/// <summary>
/// How much reach a user has, written into the token as <c>privilege_level</c>.
///
/// This is deliberately coarse and separate from roles and permissions. It answers
/// "which tier of the platform is this person?", where roles answer "what may they do?".
/// Only <see cref="SuperAdmin"/> may cross a Tenant boundary.
/// </summary>
public enum PrivilegeLevel
{
    /// <summary>An ordinary Tenant user. Everything they see is their own Organisation's.</summary>
    Standard = 0,

    /// <summary>Elevated inside one Tenant, but still one Tenant only.</summary>
    Elevated = 1,

    /// <summary>The administrator of one Organisation. Manages users, roles, menus, modules.</summary>
    TenantAdmin = 2,

    /// <summary>Root/global. Creates Organisations, approves them, and can operate inside any of them.</summary>
    SuperAdmin = 3
}
