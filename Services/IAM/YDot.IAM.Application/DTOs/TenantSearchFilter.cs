using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.DTOs;

/// <summary>
/// Filter for the SuperAdmin Organisation directory. Platform-scope only: a Tenant user never
/// reaches the endpoint that takes this.
/// </summary>
public sealed class TenantSearchFilter : PaginationRequest
{
    public TenantStatus? Status { get; set; }

    public Guid? BusinessUnitId { get; set; }

    /// <summary>Only those waiting on SuperAdmin: Submitted, Resubmitted or UnderReview.</summary>
    public bool? AwaitingReview { get; set; }

    public string? Country { get; set; }

    public string? OrganisationType { get; set; }

    public DateTimeOffset? CreatedFromUtc { get; set; }

    public DateTimeOffset? CreatedToUtc { get; set; }
}
