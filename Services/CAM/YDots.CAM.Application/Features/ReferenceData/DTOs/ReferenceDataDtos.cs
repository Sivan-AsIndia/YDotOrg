using YDots.CAM.Domain.Common.Enums;

namespace YDots.CAM.Application.Features.ReferenceData.DTOs;

/// <summary>
/// One row of a reference table: a channel, a source or a medium.
///
/// ONE RESPONSE TYPE FOR ALL THREE, replacing ChannelListResponse, SourceListResponse and
/// MediumListResponse - three records with identical fields and different names. A client that
/// renders a dropdown does not care which of the three it was handed, and three types meant
/// three copies of the same rendering code.
/// </summary>
public sealed record ReferenceItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Status Status,
    bool IsActive,
    int SortOrder);

/// <summary>
/// Everything a campaign or tracking-asset form needs to render its dropdowns, in one call.
///
/// One payload rather than three endpoints, for the reason the IAM reference-data call gives:
/// a tracking asset form needs channels, sources AND mediums before it can be drawn, and three
/// round trips is three chances to leave the form half-populated.
/// </summary>
public sealed record CampaignReferenceDataResponse(
    IReadOnlyList<ReferenceItemResponse> Channels,
    IReadOnlyList<ReferenceItemResponse> Sources,
    IReadOnlyList<ReferenceItemResponse> Mediums,
    IReadOnlyList<EnumOptionResponse> CampaignStatuses,
    IReadOnlyList<EnumOptionResponse> LifecycleActivations,
    IReadOnlyList<EnumOptionResponse> TrackingAssetTypes,
    IReadOnlyList<EnumOptionResponse> TrackingAssetStatuses,
    IReadOnlyList<EnumOptionResponse> ReadinessCategories,
    IReadOnlyList<EnumOptionResponse> ReadinessStatuses);

/// <summary>
/// One enum value, served from the server.
///
/// The client could hard-code these, and then they would drift the first time a value is added.
/// Serving them means one source of truth and a dropdown that is never stale.
/// </summary>
public sealed record EnumOptionResponse(string Value, string Label, int Ordinal);
