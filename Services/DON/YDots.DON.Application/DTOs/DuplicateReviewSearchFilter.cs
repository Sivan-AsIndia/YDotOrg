using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>Query string of the duplicate review queue (SCR-DON-004).</summary>
public sealed class DuplicateReviewSearchFilter : PaginationRequest
{
    /// <summary>Free text over review reference and candidate display names.</summary>
    public string? Search { get; set; }

    public DonorMergeCaseStatus? Status { get; set; }

    public IdentityConfidence? IdentityConfidence { get; set; }

    public MergeDecision? Decision { get; set; }

    public Guid? CandidateDonorId { get; set; }

    public DateTimeOffset? RaisedAfterUtc { get; set; }

    public DateTimeOffset? RaisedBeforeUtc { get; set; }
}
