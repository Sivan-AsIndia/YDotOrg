namespace YDots.DON.Domain.Enums;

/// <summary>
/// The stages the "Donation totals by stage" panel groups by. The amounts themselves are owned
/// by the donations section; DON only keeps a projection of them so the 360 view can be drawn
/// without a synchronous call to another service.
///
/// STORED AS A STRING (<c>HasConversion&lt;string&gt;</c> in DonorConfigurations), so these numbers
/// never reach the database and a new member can be added without a migration. It also means the
/// numbers are NOT a sort key - see <see cref="LifecycleOrder"/>.
/// </summary>
public enum DonationStage
{
    Pledged = 1,
    Committed = 2,
    Received = 3,
    Refunded = 4,
    Outstanding = 5,

    /// <summary>
    /// Money the bank has confirmed, not merely money the gateway reported.
    ///
    /// A SEPARATE STAGE FROM <see cref="Received"/>, and the distinction is the point: a gateway
    /// says a payment succeeded, and reconciliation is where that claim is matched against what
    /// actually landed. The Donor 360 panel in the module brief shows Pledged, Received and
    /// Reconciled side by side precisely so the gap between the last two is visible.
    ///
    /// The screen could never draw it before - it has always had a badge tone for "Reconciled" -
    /// because this member did not exist, so the projection had nowhere to put the figure.
    /// <c>pay.donations.reconcile</c> is the permission that produces it.
    ///
    /// APPENDED RATHER THAN SLOTTED IN AFTER Received, so no existing member is renumbered.
    /// </summary>
    Reconciled = 6
}

/// <summary>
/// The order the stages are shown in, which is the order money moves through them.
///
/// NEEDED BECAUSE THE ENUM'S NUMBERS CANNOT BE THE SORT KEY. The stage is persisted as a string,
/// so <c>OrderBy(summary =&gt; summary.Stage)</c> in a query sorts alphabetically - Committed,
/// Outstanding, Pledged, Received, Reconciled, Refunded - which puts "Outstanding" before anything
/// has been pledged and reads as though the panel were in no order at all. Appending
/// <see cref="DonationStage.Reconciled"/> as 6 would not have fixed that either, since the numbers
/// never reach the database.
/// </summary>
public static class DonationStageOrder
{
    /// <summary>Rank used to sort the panel: pledged, committed, received, reconciled, then the exceptions.</summary>
    public static int LifecycleOrder(this DonationStage stage) =>
        stage switch
        {
            DonationStage.Pledged => 0,
            DonationStage.Committed => 1,
            DonationStage.Received => 2,
            DonationStage.Reconciled => 3,
            DonationStage.Outstanding => 4,
            DonationStage.Refunded => 5,
            _ => 6
        };
}
