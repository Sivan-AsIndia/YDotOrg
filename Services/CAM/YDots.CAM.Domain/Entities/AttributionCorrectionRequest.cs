using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A request to look again at which campaign a donation was credited to.
///
/// IT RECORDS AN ASK, NOT A CHANGE. Re-attributing a gift moves money between campaigns in every
/// report that follows, so this never alters the donation - it records that somebody with grounds
/// has raised it, what they think it should be, and why. The correction itself is made where the
/// donation lives.
///
/// WHY IT IS WORTH STORING AT ALL. Without it, a fundraiser who spots a misattributed gift has
/// nowhere to put that observation except an e-mail, and the next person to look at the same
/// donation has no idea it has already been questioned. The open flag on the explorer row is the
/// whole value: it stops three people investigating the same gift independently.
/// </summary>
public sealed class AttributionCorrectionRequest : TenantEntity
{
    /// <summary>
    /// The donation in question.
    ///
    /// NOT A FOREIGN KEY, deliberately. The donation lives in the payments tables and CAM does not
    /// own it; a database-level relationship across that boundary would make one module's schema
    /// depend on another's and would block payments from ever moving to its own database.
    /// </summary>
    public Guid DonationId { get; set; }

    /// <summary>The reference, kept alongside the id so the request stays readable on its own.</summary>
    public string DonationReference { get; set; } = string.Empty;

    /// <summary>What the donation is credited to now, captured when the request was raised.</summary>
    public Guid? CurrentCampaignId { get; set; }

    public Guid? CurrentTrackingAssetId { get; set; }

    /// <summary>What the requester believes it should be credited to.</summary>
    public Guid? ProposedCampaignId { get; set; }

    public Guid? ProposedTrackingAssetId { get; set; }

    /// <summary>Why. Required, because a correction request with no reasoning cannot be assessed.</summary>
    public string Reason { get; set; } = string.Empty;

    public bool IsResolved { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>What was decided, and whether the attribution actually changed.</summary>
    public string? ResolutionNote { get; set; }

    /// <summary>
    /// Whether the attribution was changed as a result.
    ///
    /// SEPARATE FROM BEING RESOLVED. Most correction requests end with "checked, the attribution was
    /// right" - which is a resolution and not a change, and recording the two as one would make it
    /// impossible to tell how often tracking is actually getting it wrong.
    /// </summary>
    public bool AttributionChanged { get; set; }
}
