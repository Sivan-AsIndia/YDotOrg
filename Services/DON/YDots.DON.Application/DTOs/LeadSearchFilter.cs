using YDots.DON.Application.Common.Models;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.DTOs;

/// <summary>
/// Query string of the lead work queue and the assignment board. Every field here is one of
/// the "Context and filters" controls listed for SCR-DON-001 and SCR-DON-006.
/// </summary>
public sealed class LeadSearchFilter : PaginationRequest
{
    /// <summary>"Lead name or contact" search box. Matches reference, name, e-mail and phone.</summary>
    public string? Search { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? OwnerUserId { get; set; }

    public LeadStatus? Status { get; set; }

    public SlaState? SlaState { get; set; }

    public string? PreferredLanguage { get; set; }

    public string? TeamCode { get; set; }

    public WorkloadBand? WorkloadBand { get; set; }

    public ContactOutcome? LastContactOutcome { get; set; }

    public DateTimeOffset? DueBeforeUtc { get; set; }

    public DateTimeOffset? DueAfterUtc { get; set; }

    /// <summary>True returns only records the caller owns, whatever their data scope allows.</summary>
    public bool? OnlyMine { get; set; }

    /// <summary>Include drafts that Save has not yet promoted. Off by default.</summary>
    public bool IncludeDrafts { get; set; }
}
