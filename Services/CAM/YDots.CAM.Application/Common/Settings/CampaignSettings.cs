namespace YDots.CAM.Application.Common.Settings;

/// <summary>
/// The campaign rules an Organisation might reasonably want to tune, bound through the option
/// pattern so none of them is a literal buried in a handler.
/// </summary>
public sealed class CampaignSettings
{
    public const string SectionName = "CampaignSettings";

    // AutoApproveSuperAdminSubmissions WAS HERE AND HAS BEEN REMOVED.
    //
    // It promoted a platform administrator's own submission straight to Approved, which made one
    // person both submitter and approver of the same campaign. Segregation of duties is the one
    // rule this platform states cannot be granted away, so a setting whose whole purpose was to
    // grant it away could not stay - defaulting it to false would have left the hole one
    // environment variable from returning.
    //
    // "An Organisation that wants a second pair of eyes on everything" is not a preference to be
    // configured; it is the rule. Every other approval path in CAM, DON and PAY already enforced
    // it against the same account.

    /// <summary>
    /// Whether a campaign may go live with a required readiness check still outstanding.
    ///
    /// Off by default, which is the safe direction: the checklist exists to stop a campaign
    /// launching without its payment configuration or its consent wording.
    /// </summary>
    public bool AllowLaunchWithOutstandingChecks { get; set; }

    /// <summary>How long a campaign may run. Guards a typo that would schedule one for a decade.</summary>
    public int MaximumCampaignDurationDays { get; set; } = 1095;

    /// <summary>
    /// How many days before the start date the activation reminder fires, when a campaign does
    /// not set its own.
    /// </summary>
    public int DefaultDaysBeforeStart { get; set; } = 7;

    /// <summary>
    /// The base a generated tracking URL is built on, for example https://give.ngoplanet.com.
    ///
    /// It is configuration rather than a constant because it differs per environment, and a
    /// tracking link that points at a staging host in a printed QR code is not recoverable.
    /// </summary>
    public string TrackingBaseUrl { get; set; } = string.Empty;
}
