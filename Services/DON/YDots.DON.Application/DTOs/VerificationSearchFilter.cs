using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>Query string of the donor identity verification screen (DON-UI-07).</summary>
public sealed class VerificationSearchFilter : PaginationRequest
{
    /// <summary>Free text over verification reference and donor reference.</summary>
    public string? Search { get; set; }

    public Guid? DonorId { get; set; }

    public VerificationStatus? Status { get; set; }

    public VerificationChannel? Channel { get; set; }

    public IdentityConfidence? IdentityConfidence { get; set; }

    public Guid? ReviewerUserId { get; set; }

    public DateTimeOffset? SentAfterUtc { get; set; }

    public DateTimeOffset? SentBeforeUtc { get; set; }
}
