namespace YDots.CAM.Domain.Enums;

/// <summary>
/// The lifecycle of one version of a budget and target plan.
///
/// <see cref="Superseded"/> is the state that makes versioning safe. When a newer version is
/// approved, the one it replaces moves here rather than being deleted or left as Approved. Deleting
/// it would lose the figures a decision was actually taken against; leaving it approved would let
/// two versions of the same plan both count toward a campaign's target, which is exactly the double
/// counting the versioning exists to prevent.
/// </summary>
public enum PlanApprovalState
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Superseded = 4,
    Rejected = 5
}
