namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// The custom claim types IAM writes into the access token. DON only reads them, so these
/// names have to match YDots.IAM.Application/Common/Constants/ClaimTypeNames.cs exactly.
/// If IAM adds a claim, add the same constant here before DON can use it.
/// </summary>
public static class ClaimTypeNames
{
    public const string UserId = "sub";
    public const string UserCode = "user_code";
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

    /// <summary>
    /// The Organisation, under the name the other three services use.
    ///
    /// DON reads <c>organisation_id</c> as its isolation boundary and IAM emits both names for
    /// exactly that reason. This constant exists so the two can be read interchangeably rather
    /// than DON silently depending on the older of the two spellings.
    /// </summary>
    public const string TenantId = "tenant_id";

    public const string BusinessUnitId = "business_unit_id";

    /// <summary>
    /// THE PLATFORM ROOT.
    ///
    /// DON did not read this claim, and the omission had teeth: <c>HasPermission</c> was a plain
    /// set lookup, so a SuperAdmin was refused every DON endpoint unless somebody had also
    /// assigned them every individual <c>don.*</c> permission. IAM, CAM and PAY all let the root
    /// user through; DON was the one service that did not.
    /// </summary>
    public const string IsSuperAdmin = "is_super_admin";

    public const string IsTenantAdmin = "is_tenant_admin";
}
