using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>Query string of the consent and preference centre (SCR-DON-005).</summary>
public sealed class ConsentSearchFilter : PaginationRequest
{
    /// <summary>Free text over donor reference, donor name and purpose.</summary>
    public string? Search { get; set; }

    public Guid? DonorId { get; set; }

    public Guid? LeadId { get; set; }

    public ConsentChannel? Channel { get; set; }

    public ConsentState? ConsentState { get; set; }

    public ConsentStatus? Status { get; set; }

    public string? NoticeVersion { get; set; }

    public DateTimeOffset? EffectiveAfterUtc { get; set; }

    public DateTimeOffset? EffectiveBeforeUtc { get; set; }

    /// <summary>True includes superseded and withdrawn rows, which is what the history panel wants.</summary>
    public bool IncludeHistory { get; set; }
}
