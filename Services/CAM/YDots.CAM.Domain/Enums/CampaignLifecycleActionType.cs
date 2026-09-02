namespace YDots.CAM.Domain.Enums;

/// <summary>
/// The transitions a campaign is taken through, as the lifecycle table records them.
///
/// SUBMIT AND APPROVE WERE MISSING, and their absence was not cosmetic. The submit handler wrote
/// its lifecycle row as <see cref="Activate"/> because that was the closest value on the enum, so
/// the history tab reported "Activate" against a campaign that had merely been sent for approval
/// - and a campaign that was later genuinely activated had two identical-looking rows describing
/// two different events. The column is stored as text, so naming the two properly costs no
/// migration.
/// </summary>
public enum CampaignLifecycleActionType
{
    Activate = 1,
    Pause = 2,
    Resume = 3,
    RequestClose = 4,
    ApproveClose = 5,
    CancelDraft = 6,

    /// <summary>Draft to Submitted: sent for approval.</summary>
    Submit = 7,

    /// <summary>Submitted to Scheduled or Approved: the launch decision itself.</summary>
    Approve = 8,

    /// <summary>Back to Draft from the readiness screen, with a reason.</summary>
    ReturnToDraft = 9
}
