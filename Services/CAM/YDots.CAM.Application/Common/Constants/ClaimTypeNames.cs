namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The custom claim types IAM writes into the access token. CAM only READS them, so these names
/// have to match <c>YDot.IAM.Application/Common/Constants/ClaimTypeNames.cs</c> exactly.
///
/// If IAM adds a claim, add the same constant here before CAM can use it. Add rather than
/// rename: a new claim is merely invisible to a service that does not know about it, while a
/// renamed one is invisible to a service that does - and the symptom is a 403 on an endpoint
/// that looks correctly configured.
/// </summary>
public static class ClaimTypeNames
{
    public const string UserId = "sub";
    public const string UserCode = "user_code";

    /// <summary>
    /// The Organisation the token is operating in. IAM writes the same value into
    /// <c>organisation_id</c> as well, for the services that predate the tenancy vocabulary;
    /// CAM reads <c>tenant_id</c> first and falls back.
    /// </summary>
    public const string TenantId = "tenant_id";

    public const string OrganisationId = "organisation_id";
    public const string TenantCode = "tenant_code";
    public const string TenantName = "tenant_name";
    public const string BusinessUnitId = "business_unit_id";

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
    public const string Scope = "scope";
    public const string TenantMode = "tenant_mode";
    public const string SecurityStamp = "security_stamp";
    public const string IsSuperAdmin = "is_super_admin";
    public const string IsTenantAdmin = "is_tenant_admin";
}
