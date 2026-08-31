namespace YDot.IAM.Domain.Enums;

/// <summary>
/// Who the invitation is for. The distinction matters because accepting a
/// <see cref="TenantAdmin"/> invitation also moves the Organisation's own lifecycle
/// forward (Invited -> InvitationAccepted -> ProfileIncomplete), whereas accepting a
/// <see cref="TenantUser"/> invitation only activates that one person.
/// </summary>
public enum InvitationType
{
    /// <summary>The first administrator of a new Organisation, invited by SuperAdmin.</summary>
    TenantAdmin = 0,

    /// <summary>An ordinary user invited by their own TenantAdmin.</summary>
    TenantUser = 1,

    /// <summary>A future donor-portal account created off the back of a payment.</summary>
    DonorPortal = 2
}
