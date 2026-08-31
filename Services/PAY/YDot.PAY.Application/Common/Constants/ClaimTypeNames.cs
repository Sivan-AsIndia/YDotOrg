namespace YDot.PAY.Application.Common.Constants;

/// <summary>
/// The custom claim types IAM writes into the access token. PAY only READS them, so these names
/// must match <c>YDot.IAM.Application/Common/Constants/ClaimTypeNames.cs</c> exactly.
///
/// Add rather than rename: a new claim is merely invisible to a service that does not know about
/// it, while a renamed one is invisible to a service that does - and the symptom is a 403 on an
/// endpoint that looks correctly configured.
/// </summary>
public static class ClaimTypeNames
{
    public const string UserId = "sub";
    public const string UserCode = "user_code";
    public const string TenantId = "tenant_id";
    public const string OrganisationId = "organisation_id";
    public const string TenantCode = "tenant_code";
    public const string TenantName = "tenant_name";
    public const string BusinessUnitId = "business_unit_id";
    public const string DisplayName = "display_name";
    public const string Username = "username";
    public const string Email = "email";
    public const string SessionId = "session_id";
    public const string Permission = "permission";
    public const string DataScope = "data_scope";
    public const string UserStatus = "user_status";
    public const string TokenType = "token_type";
    public const string Scope = "scope";
    public const string IsSuperAdmin = "is_super_admin";
    public const string IsTenantAdmin = "is_tenant_admin";
}
