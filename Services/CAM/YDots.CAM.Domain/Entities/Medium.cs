using YDots.CAM.Domain.Common;
using YDots.CAM.Domain.Common.Enums;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// How they arrived, in the UTM sense: cpc, organic, referral, banner.
///
/// A GLOBAL REFERENCE TABLE, deliberately NOT <see cref="ITenantOwned"/>. These codes end up in
/// tracking URLs and in attribution reporting that spans Organisations, so one code has to mean
/// one thing platform-wide. Giving each Organisation its own copy would let two of them define
/// CPC differently and make a cross-Organisation report meaningless.
///
/// It is maintained by SuperAdmin. An Organisation reads it and cannot change it.
/// </summary>
public class Medium : AuditEntity, ICodedEntity
{
    /// <summary>Unique platform-wide, and stable: it appears in generated tracking URLs.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Status Status { get; set; } = Status.Active;

    /// <summary>Controls the order it appears in a picker. Ties fall back to the name.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only an Active row is offered for selection on a form.</summary>
    public bool IsSelectable => Status == Status.Active;
}
