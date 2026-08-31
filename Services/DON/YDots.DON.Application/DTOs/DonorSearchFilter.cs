using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>Query string of GET /api/v1/donors. Bound straight from [FromQuery].</summary>
public sealed class DonorSearchFilter : PaginationRequest
{
    /// <summary>Free text over donor number, name, organisation name, e-mail and phone.</summary>
    public string? Search { get; set; }

    public DonorType? DonorType { get; set; }

    public DonorStatus? Status { get; set; }

    public ApprovalState? ApprovalState { get; set; }

    public string? PreferredLanguage { get; set; }

    public bool? DoNotContact { get; set; }

    public Guid? RelationshipOwnerUserId { get; set; }

    /// <summary>Filter by an attached tag code, for example MAJOR_GIVER.</summary>
    public string? TagCode { get; set; }

    public DateTimeOffset? UpdatedAfterUtc { get; set; }

    public DateTimeOffset? UpdatedBeforeUtc { get; set; }
}
