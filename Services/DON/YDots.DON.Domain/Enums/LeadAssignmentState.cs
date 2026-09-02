namespace YDots.DON.Domain.Enums;

/// <summary>
/// Whether a lead has an owner, as the Lead Queue's tabs ask it.
///
/// TWO STATES, NOT THREE. There is no "partly assigned": a lead either has an OwnerUserId or it
/// does not, and the Assignment Board's whole purpose is moving one to the other. The absent
/// third value is the filter being unset, which is the All Leads tab.
/// </summary>
public enum LeadAssignmentState
{
    /// <summary>No owner. These are the rows the Lead Queue offers an Assign action on.</summary>
    Unassigned = 1,

    /// <summary>Has an owner. The Assignment Board offers Reassign on these instead.</summary>
    Assigned = 2,
}
