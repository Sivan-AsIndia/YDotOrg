using Microsoft.AspNetCore.Identity;
using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// An external sign-in provider linked to a user: Google, Microsoft, a future SAML or OIDC
/// identity provider.
///
/// A CUSTOMISED IdentityCore TABLE, deriving from <see cref="IdentityUserLogin{TKey}"/>, so
/// <c>LoginProvider</c>, <c>ProviderKey</c>, <c>ProviderDisplayName</c> and <c>UserId</c>
/// come from the base and the framework external-login flow works unchanged.
///
/// WHY IT IS HERE NOW, BEFORE ANY PROVIDER IS WIRED UP. Nothing in the application signs in
/// through an external provider yet. The table exists because IdentityCore expects it, and
/// because the alternative — adding it later — means a schema migration on the identity
/// tables of a live multi-tenant system, which is precisely the migration nobody wants to
/// run. It costs one empty table now.
///
/// THE TENANCY MATTERS MORE HERE THAN ANYWHERE. An external provider hands back an e-mail
/// address, and an e-mail address is NOT an identity in this system: the same Google account
/// legitimately maps to a different user in every Organisation it belongs to. So the link is
/// per Tenant, and resolving an external login must always start from the Organisation the
/// request arrived at, never from the address alone.
/// </summary>
public class UserLogin : IdentityUserLogin<Guid>, ITenantScoped
{
    /// <summary>
    /// Nullable so the row can belong to the global SuperAdmin or a Platform role, which
    /// belong to no Organisation. Matches the nullability of User.TenantId and Role.TenantId.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Non-null mirror of <see cref="TenantId"/> (Guid.Empty means the platform), maintained
    /// by the DbContext. It carries the alternate key that the Tenant-scoped composite
    /// foreign keys point at. See <see cref="ITenantScoped.TenantKey"/> for why it is needed.
    /// </summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    public User? User { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    /// <summary>Lets a link be suspended without unlinking, which is reversible.</summary>
    public bool IsActive { get; set; } = true;
}
