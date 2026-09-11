namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The kinds of organisation the platform offers when one is set up or its profile is edited.
///
/// A LIST AND NOT AN ENUM, because <c>Tenant.OrganisationType</c> is a string column that already
/// holds these exact labels, and every organisation created so far was written with one of them.
/// An enum would need a migration to say the same thing.
///
/// SERVED FROM HERE so the setup wizard and the organisation profile offer the same choices. They
/// were a literal array in the wizard and a free-text box on the profile, so an organisation typed
/// "NGO" on one screen and picked "Non-profit / NGO" on the other, and the directory could group
/// neither. "Other" ends the list, which is what makes it tolerable to constrain.
/// </summary>
public static class OrganisationTypes
{
    public static readonly IReadOnlyList<string> All =
    [
        "Non-profit / NGO",
        "Charitable organisation",
        "Foundation",
        "Community organisation",
        "Educational organisation",
        "Healthcare organisation",
        "Faith-based organisation",
        "Social welfare organisation",
        "International organisation",
        "Other",
    ];
}
