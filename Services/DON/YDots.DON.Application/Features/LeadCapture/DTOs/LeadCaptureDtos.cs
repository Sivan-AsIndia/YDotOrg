using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Features.Leads.DTOs;

namespace YDots.DON.Application.Features.LeadCapture.DTOs;

/// <summary>
/// GET /api/v1/donors/lead-capture. One call returns the form context: the record being
/// edited if there is one, every catalogue the selectors need, the actions the caller may
/// take and the screen state. The UI never has to make five calls to draw one screen.
/// </summary>
public sealed record LeadCaptureResponse(
    string ScreenId,
    string Route,
    LeadDetailResponse? Lead,
    IReadOnlyList<LookupItem> CampaignOptions,
    IReadOnlyList<LookupItem> LanguageOptions,
    IReadOnlyList<LookupItem> ConsentChannelOptions,
    IReadOnlyList<LookupItem> ConsentStateOptions,
    IReadOnlyList<LookupItem> OwnerOptions,
    string CurrentNoticeVersion,
    IReadOnlyList<DuplicateCandidateResponse> DuplicateCandidates,
    IReadOnlyList<string> PermittedActions,
    string ActiveScope,
    string State);
