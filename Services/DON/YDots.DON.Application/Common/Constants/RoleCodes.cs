namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// The Donors roles IAM seeds. DON never assigns a role — it only recognises the codes that
/// arrive as role claims, mainly so a screen payload can say who the eligible owners are.
/// Mirrors ModulePermissionCatalogue.Roles in IAM.
/// </summary>
public static class RoleCodes
{
    public const string Fundraiser = "FUNDRAISER";
    public const string FundraisingManager = "FUNDRAISING_MANAGER";
    public const string RelationshipUser = "RELATIONSHIP_USER";
    public const string DataSteward = "DATA_STEWARD";
    public const string DonorCare = "DONOR_CARE";
    public const string AuthorisedStaff = "AUTHORISED_STAFF";
    public const string SystemIntegration = "SYSTEM_INTEGRATION";

    public static readonly IReadOnlyList<string> All =
    [
        Fundraiser, FundraisingManager, RelationshipUser, DataSteward,
        DonorCare, AuthorisedStaff, SystemIntegration
    ];
}
