using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.DTOs;

/// <summary>
/// Filter for the user directory grid.
///
/// Note what is NOT here: TenantId. The Organisation is never a filter the caller supplies —
/// it comes from the token, and letting it arrive on a query string is exactly the mistake
/// section 47 of the brief warns against.
/// </summary>
public sealed class UserSearchFilter : PaginationRequest
{
    public UserStatus? Status { get; set; }

    public UserAccountCategory? AccountCategory { get; set; }

    public Guid? DepartmentId { get; set; }

    public Guid? OrganisationUnitId { get; set; }

    public Guid? RoleId { get; set; }

    public Guid? ManagerUserId { get; set; }

    public MfaRequirement? MfaRequirement { get; set; }

    /// <summary>Only accounts currently locked out.</summary>
    public bool? IsLockedOut { get; set; }

    /// <summary>Only accounts that have never signed in. Finds stalled invitations.</summary>
    public bool? NeverSignedIn { get; set; }

    /// <summary>Accounts whose access window closes before this date.</summary>
    public DateTimeOffset? AccessEndingBefore { get; set; }

    public DateTimeOffset? CreatedFromUtc { get; set; }

    public DateTimeOffset? CreatedToUtc { get; set; }
}
