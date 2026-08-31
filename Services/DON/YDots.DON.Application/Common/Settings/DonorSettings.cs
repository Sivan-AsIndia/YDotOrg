namespace YDots.DON.Application.Common.Settings;

/// <summary>
/// Bound from the DonorSettings section of appsettings.json. Everything the Donors section
/// needs to tune without a rebuild: numbering prefixes, SLA thresholds, workload bands and
/// the identity verification challenge rules.
/// </summary>
public sealed class DonorSettings
{
    public const string SectionName = "DonorSettings";

    /// <summary>Prefix of the generated donor number, giving DON-2026-000184.</summary>
    public string DonorNumberPrefix { get; set; } = "DON";

    /// <summary>Prefix of the generated lead reference, giving LED-2026-000317.</summary>
    public string LeadReferencePrefix { get; set; } = "LED";

    /// <summary>Prefix of the generated duplicate review reference.</summary>
    public string MergeCaseReferencePrefix { get; set; } = "DUP";

    /// <summary>Prefix of the generated identity verification reference.</summary>
    public string VerificationReferencePrefix { get; set; } = "VER";

    /// <summary>Prefix of the generated follow-up reference.</summary>
    public string FollowUpReferencePrefix { get; set; } = "FUP";

    /// <summary>How many hours before the due time the SLA badge turns from OnTrack to DueToday.</summary>
    public int SlaDueSoonHours { get; set; } = 24;

    /// <summary>How many hours past the due time the badge turns from Overdue to Breached.</summary>
    public int SlaBreachHours { get; set; } = 72;

    /// <summary>Open-work count at or below which an owner counts as Light.</summary>
    public int WorkloadLightThreshold { get; set; } = 5;

    /// <summary>Open-work count at or below which an owner counts as Balanced.</summary>
    public int WorkloadBalancedThreshold { get; set; } = 15;

    /// <summary>Open-work count at or below which an owner counts as Heavy; above it, Overloaded.</summary>
    public int WorkloadHeavyThreshold { get; set; } = 30;

    /// <summary>How long an identity verification challenge stays valid.</summary>
    public int VerificationCodeValidMinutes { get; set; } = 10;

    /// <summary>How many wrong codes are accepted before the attempt fails.</summary>
    public int VerificationMaxAttempts { get; set; } = 5;

    /// <summary>Number of digits in the challenge code.</summary>
    public int VerificationCodeDigits { get; set; } = 6;

    /// <summary>Maximum number of leads one Bulk route action may move.</summary>
    public int BulkRouteMaximumItems { get; set; } = 100;

    /// <summary>Maximum number of rows a controlled export may contain.</summary>
    public int ExportMaximumRows { get; set; } = 5000;

    /// <summary>The current privacy notice version stamped on every consent row.</summary>
    public string CurrentNoticeVersion { get; set; } = "PN-2026-01";
}
