namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The custom claim types IAM writes into the access token.
///
/// THIS FILE IS A CROSS-SERVICE CONTRACT. IAM is the only service that signs a token, so it
/// is the only service that can put a claim into one; every other service only reads them.
/// The first block below is mirrored in
/// <c>YDots.DON.Application/Common/Constants/ClaimTypeNames.cs</c> and must keep matching it
/// exactly — a rename here silently breaks authorisation there, because the claim the other
/// service looks for simply stops arriving.
///
/// Add rather than rename. A new claim is invisible to a service that does not know about
/// it; a renamed one is invisible to a service that does.
/// </summary>
public static class ClaimTypeNames
{
    // ---- Mirrored in DON. Do not rename. -----------------------------------------------

    public const string UserId = "sub";
    public const string UserCode = "user_code";

    /// <summary>
    /// KEPT FOR COMPATIBILITY. DON reads <c>organisation_id</c> and treats it as the
    /// isolation boundary. In the tenancy model that boundary is the Tenant, so IAM writes
    /// the SAME value into both this claim and <see cref="TenantId"/>. Doing so means the
    /// Donors service keeps working unchanged while IAM moves to the new vocabulary, and
    /// nothing has to be deployed in lockstep.
    /// </summary>
    public const string OrganisationId = "organisation_id";

    public const string OrganisationUnitId = "organisation_unit_id";
    public const string DepartmentId = "department_id";
    public const string DisplayName = "display_name";
    public const string Username = "username";
    public const string Email = "email";
    public const string SessionId = "session_id";
    public const string ClientType = "client_type";
    public const string Permission = "permission";
    public const string DataScope = "data_scope";
    public const string AccountCategory = "account_category";
    public const string UserStatus = "user_status";
    public const string MfaCompleted = "mfa_completed";
    public const string PrivilegeLevel = "privilege_level";
    public const string TokenType = "token_type";
    public const string AuthenticatedAt = "auth_time";

    // ---- Tenancy. New in this build. ------------------------------------------------------

    /// <summary>
    /// The Organisation the token is currently operating in. For a Tenant user this is their
    /// own and never changes. For SuperAdmin it is whichever Organisation they selected, and
    /// it changes when they switch — while their persistent User.TenantId stays null.
    /// </summary>
    public const string TenantId = "tenant_id";

    public const string TenantCode = "tenant_code";
    public const string TenantName = "tenant_name";

    /// <summary>The root boundary above Tenant.</summary>
    public const string BusinessUnitId = "business_unit_id";

    public const string BusinessUnitCode = "business_unit_code";

    /// <summary>
    /// "Global" for SuperAdmin, "Tenant" for everybody else. The single most important
    /// authorisation fact in the token.
    /// </summary>
    public const string Scope = "scope";

    /// <summary>
    /// True when a Global-scope caller has selected an Organisation and is operating inside
    /// it. Lets an endpoint tell "SuperAdmin acting as TEN001" apart from "SuperAdmin doing
    /// platform work", which the audit trail needs to record differently.
    /// </summary>
    public const string TenantMode = "tenant_mode";

    /// <summary>The host the token was issued for, so a token cannot be replayed at another.</summary>
    public const string HostName = "host_name";

    /// <summary>
    /// The user security stamp at issue time. Any credential, role or status change
    /// regenerates it, and a token whose stamp no longer matches is refused even though its
    /// signature and expiry are still valid. This is what makes revocation immediate rather
    /// than a promise that expires with the token.
    /// </summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>True when this caller is the platform root user.</summary>
    public const string IsSuperAdmin = "is_super_admin";

    /// <summary>True when this caller administers their own Organisation.</summary>
    public const string IsTenantAdmin = "is_tenant_admin";

    /// <summary>Device identifier reported by a mobile client, when one was supplied.</summary>
    public const string DeviceIdentifier = "device_id";
}
