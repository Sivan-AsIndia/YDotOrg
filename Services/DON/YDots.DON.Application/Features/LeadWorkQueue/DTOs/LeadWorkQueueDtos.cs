using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Features.Leads.DTOs;

namespace YDots.DON.Application.Features.LeadWorkQueue.DTOs;

/// <summary>
/// GET /api/v1/donors/lead-work-queue. Rows, every filter option, the totals qualified by
/// scope and the actions the caller may take, in one call.
/// </summary>
public sealed record LeadWorkQueueResponse(
    string ScreenId,
    string Route,
    PagedResponse<LeadListItemResponse> Leads,
    IReadOnlyList<LookupItem> CampaignOptions,
    IReadOnlyList<LookupItem> OwnerOptions,
    IReadOnlyList<LookupItem> StatusOptions,
    IReadOnlyList<LookupItem> SlaStateOptions,
    IReadOnlyList<LookupItem> LanguageOptions,
    IReadOnlyList<LookupItem> ContactOutcomeOptions,
    IReadOnlyDictionary<string, int> StatusCounts,

    /// <summary>The six summary cards, counted across the whole scope rather than the page.</summary>
    LeadQueueSummaryResponse Summary,

    /// <summary>Cold / Warm / Hot, for the temperature column filter.</summary>
    IReadOnlyList<LookupItem> TemperatureOptions,

    /// <summary>Low / Medium / High, for the donation-potential column filter.</summary>
    IReadOnlyList<LookupItem> DonationPotentialOptions,

    IReadOnlyList<string> PermittedActions,
    string ActiveFilterSummary,
    string ActiveScope,
    DateTimeOffset LastRefreshedAtUtc,
    string State);

/// <summary>POST .../accept. No body beyond the optional note; the caller becomes the owner.</summary>
public sealed class AcceptLeadRequest
{
    public string? Comment { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../assign. Moves ownership and always records why.</summary>
public sealed class AssignLeadRequest
{
    public Guid NewOwnerUserId { get; set; }

    public string NewOwnerName { get; set; } = string.Empty;

    public string? TeamCode { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string AssignmentReason { get; set; } = string.Empty;

    public DateTimeOffset? EffectiveAtUtc { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../contact. Records one conversation and its outcome.</summary>
public sealed class ContactLeadRequest
{
    /// <summary>Which channel was actually used. Checked against the consent rows.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>The result of the conversation, from the ContactOutcome catalogue.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTimeOffset? OccurredAtUtc { get; set; }

    public string? NextAction { get; set; }

    public DateTimeOffset? NextActionDueUtc { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>POST .../qualify. Records the disposition that makes the lead ready to convert.</summary>
public sealed class QualifyLeadRequest
{
    /// <summary>Required. 10 to 2000 characters.</summary>
    public string QualificationNotes { get; set; } = string.Empty;

    public string? NextAction { get; set; }

    public DateTimeOffset? NextActionDueUtc { get; set; }

    /// <summary>True parks the lead in Nurture instead of moving it to Qualified.</summary>
    public bool MoveToNurture { get; set; }

    public long? ExpectedVersion { get; set; }
}

/// <summary>
/// POST .../convert. Step 5 of the guided flow: "Establish relationship — create or link the
/// donor profile and preserve lead history and attribution."
/// </summary>
public sealed class ConvertLeadRequest
{
    /// <summary>Link to this existing donor instead of creating a new one.</summary>
    public Guid? ExistingDonorId { get; set; }

    /// <summary>Required when a new donor is created. Defaults to Individual.</summary>
    public string? DonorType { get; set; }

    /// <summary>Required. 10 to 2000 characters.</summary>
    public string ConversionReason { get; set; } = string.Empty;

    public long? ExpectedVersion { get; set; }
}

/// <summary>
/// The summary cards across the top of the lead work queue.
///
/// COUNTED OVER THE WHOLE SCOPE, NOT THE CURRENT PAGE, which is the entire reason this is a
/// server-side aggregate rather than arithmetic in the browser. The grid is paged, so a card
/// computed from the rows in hand would read "Total Leads 10" against a queue of two hundred and
/// change every time somebody turned a page. Each card carries the same scope filter as the rows,
/// so a caller never sees a count that includes work they cannot open.
/// </summary>
public sealed record LeadQueueSummaryResponse(
    int TotalLeads,
    int UnassignedLeads,
    int AssignedLeads,
    int HotLeads,
    int ConvertedLeads,
    int HighDonationPotential);
