namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Controls what a fresh database is initialised with.
///
/// The seeder is idempotent: it reconciles rather than inserting blindly, so it can run on
/// every start without duplicating anything. That is what lets a new permission or menu node
/// appear simply by deploying, with no hand-written migration.
/// </summary>
public sealed class SeedSettings
{
    public const string SectionName = "SeedSettings";

    /// <summary>Master switch for everything below.</summary>
    public bool Enabled { get; set; } = true;

    // ---- BusinessUnit -------------------------------------------------------------------
    public string BusinessUnitCode { get; set; } = "BU001";

    public string BusinessUnitName { get; set; } = "NGoPlanet";

    /// <summary>The apex domain every Organisation subdomain hangs off.</summary>
    public string RootDomain { get; set; } = "ngoplanet.com";

    // ---- SuperAdmin -----------------------------------------------------------------------
    public string SuperAdminEmail { get; set; } = "rajat.sivan@gmail.com";

    public string SuperAdminUsername { get; set; } = "superadmin";

    public string SuperAdminFirstName { get; set; } = "Rajat";

    public string SuperAdminLastName { get; set; } = "Sivan";

    /// <summary>
    /// Development convenience only. In any real deployment leave this empty and the seeder
    /// creates the account with no password, so the only way in is the invitation link.
    /// </summary>
    public string SuperAdminPassword { get; set; } = string.Empty;

    // ---- Sample Organisations -------------------------------------------------------------
    /// <summary>Creates the sample Organisation the demonstration starts from.</summary>
    public bool SeedSampleTenants { get; set; } = true;

    /// <summary>
    /// The id given to the ACTIVATED sample Organisation, rather than a generated one.
    ///
    /// WHY THIS IS CONFIGURATION AND NOT A GENERATED GUID. DON seeds its own demonstration
    /// donors, leads and campaigns, and stamps every one of them with an OrganisationId it reads
    /// from its OWN settings. Every screen in the donor module then filters on the OrganisationId
    /// carried in the caller's token. If the two values differ, DON's sample data exists in the
    /// database and is returned to nobody: Donor List, Lead Work Queue and Donor 360 all open
    /// empty on a freshly seeded platform, with no error anywhere to explain why.
    ///
    /// IT IS DELIBERATELY THE SAME KIND OF SETTING ON BOTH SIDES, so the two are set together
    /// from one place. docker-compose passes a single SAMPLE_ORGANISATION_ID value into
    /// SeedSettings__SampleOrganisationId here and SeedSettings__OrganisationId in DON, which is
    /// what stops the pair drifting apart the way a constant compiled into each service would.
    ///
    /// It is only ever read on a database that has never been seeded. Changing it afterwards
    /// renames nothing and moves nothing - the Organisation keeps the id it was created with.
    /// </summary>
    public Guid SampleOrganisationId { get; set; } =
        Guid.Parse("9fb11890-a08e-4adc-95ca-8e4d71f4dd21");

    /// <summary>
    /// The password shared by the seeded demonstration role accounts.
    ///
    /// Separate from the SuperAdmin password on purpose: these eleven are demonstration logins
    /// that appear in a document, and the platform administrator's credential should not be the
    /// same string as something written in a guide. Leave it empty and the accounts are not
    /// seeded at all, which is what any deployment that is not a demonstration should do.
    /// </summary>
    public string RoleAccountPassword { get; set; } = string.Empty;

    /// <summary>Seeds the global permission catalogue and the menu catalogue.</summary>
    public bool SeedCatalogues { get; set; } = true;
}
