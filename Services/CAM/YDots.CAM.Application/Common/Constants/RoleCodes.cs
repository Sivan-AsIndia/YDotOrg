namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The campaign role codes, mirrored from IAM where the roles themselves are created.
///
/// CAM DOES NOT CREATE OR ASSIGN ROLES. It reads role claims off the token to answer questions
/// that permissions alone cannot - notably "is this person a Campaign Manager?", which the
/// tiered approval rule in section 5 of the module brief turns on. The authoritative
/// definitions live in <c>YDot.IAM.Application/Common/Constants/RoleCodes.cs</c> and
/// <c>TenantRoleDefinitions</c>.
/// </summary>
public static class RoleCodes
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string TenantAdmin = "TENANT_ADMIN";
    public const string CampaignManager = "CAMPAIGN_MANAGER";
    public const string CampaignOwner = "CAMPAIGN_OWNER";
}
