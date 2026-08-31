using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.ReferenceData.Queries;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.DTOs;

// =====================================================================================
// Shared across all five masters
// =====================================================================================

/// <summary>
/// The filter every master grid sends.
///
/// <see cref="Scope"/> is the one that matters. The read is always "platform rows plus my
/// own" — that is what the query filter allows — and this lets the screen narrow the view to
/// one side of it, so an administrator can see at a glance which rows are theirs to edit.
/// </summary>
public class GlobalMasterSearchFilter : PaginationRequest
{
    public MasterDataStatus? Status { get; set; }

    /// <summary>Which side of the shared catalogue to show. Defaults to both.</summary>
    public MasterRowScope Scope { get; set; } = MasterRowScope.All;
}

/// <summary>Which rows of the shared catalogue a list should return.</summary>
public enum MasterRowScope
{
    /// <summary>Platform rows and the caller's own. The default, and what the filter allows.</summary>
    All = 0,

    /// <summary>Only the seeded platform catalogue.</summary>
    Platform = 1,

    /// <summary>Only rows this Organisation added for itself.</summary>
    Tenant = 2
}

/// <summary>
/// Activating or deactivating a master row.
///
/// <c>ExpectedVersion</c> is mandatory rather than optional. Two administrators looking at
/// the same country list is the ordinary case, not the unusual one, and a lost update on a
/// currency's decimal places is the kind of thing nobody notices until a receipt is wrong.
/// </summary>
public sealed record ChangeMasterStatusRequest(
    MasterDataStatus Status,
    long ExpectedVersion,
    string? Reason = null);

/// <summary>
/// The body of an activate or deactivate call.
///
/// IT CARRIES NO STATUS, which is the point. The ROUTE says which direction the change goes -
/// <c>/activate</c> or <c>/deactivate</c> - and each route carries its own permission, so an
/// Organisation can grant the ability to switch a master back on without also granting the
/// ability to switch it off. A status in the body would make the two routes interchangeable
/// and the permission split meaningless.
/// </summary>
public sealed record MasterStatusChangeRequest(long ExpectedVersion, string? Reason = null);

/// <summary>Deleting a master row. Refused when anything still points at it.</summary>
public sealed record DeleteMasterRequest(long ExpectedVersion, string? Reason = null);

/// <summary>
/// One row of a master picker.
///
/// Deliberately NOT <see cref="LookupItem"/>, which the IAM screens use: a master picker
/// also has to show whether a row is the caller's own, because that decides whether the edit
/// pencil is drawn beside it.
/// </summary>
public sealed record MasterLookupResponse(
    Guid Id,
    string Code,
    string Name,
    MasterDataStatus Status,
    bool IsPlatformRow,
    int SortOrder);

/// <summary>
/// Everything the Masters screens need to render their dropdowns, in one call.
///
/// One payload rather than five endpoints for the same reason the IAM reference-data call is
/// one payload: a City form needs countries, states and time zones before it can be drawn,
/// and three round trips is three chances to leave the form half-populated.
/// </summary>
public sealed record GlobalMasterReferenceDataResponse(
    IReadOnlyList<MasterLookupResponse> Countries,
    IReadOnlyList<MasterLookupResponse> StateProvinces,
    IReadOnlyList<MasterLookupResponse> Currencies,
    IReadOnlyList<MasterLookupResponse> TimeZones,
    IReadOnlyList<EnumOption> Regions,
    IReadOnlyList<EnumOption> JurisdictionTypes,
    IReadOnlyList<EnumOption> CurrencyTypes,
    IReadOnlyList<EnumOption> SymbolPositions,
    IReadOnlyList<EnumOption> RoundingModes,
    IReadOnlyList<EnumOption> Statuses);
