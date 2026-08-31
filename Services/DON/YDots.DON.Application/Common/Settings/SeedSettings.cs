namespace YDots.DON.Application.Common.Settings;

/// <summary>
/// Bound from the SeedSettings section of appsettings.json.
///
/// OrganisationId HAS TO MATCH IAM's SeedSettings:SampleOrganisationId. The demonstration donors
/// and leads are stamped with it, and a real user's token carries whatever IAM gave the seeded
/// Organisation — if the two differ, those records sit outside everybody's data scope and Donor
/// List, Lead Work Queue and Donor 360 all open empty on a fresh database, with nothing anywhere
/// to explain why.
///
/// THE TWO ARE SET FROM ONE PLACE so they cannot drift. docker-compose passes a single
/// SAMPLE_ORGANISATION_ID into SeedSettings__OrganisationId here and
/// SeedSettings__SampleOrganisationId in IAM. The defaults below match the IAM default, so the
/// platform is correct with no .env at all; overriding one without the other is the mistake this
/// arrangement exists to prevent.
/// </summary>
public sealed class SeedSettings
{
    public const string SectionName = "SeedSettings";

    /// <summary>The YDot organisation. Matches IAM SeedSettings:OrganisationId.</summary>
    public Guid OrganisationId { get; set; } = Guid.Parse("9fb11890-a08e-4adc-95ca-8e4d71f4dd21");

    /// <summary>The IAM system user, used as the actor on every seeded row.</summary>
    public Guid SystemUserId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Display name written onto the seeded owner fields.</summary>
    public string SystemUserDisplayName { get; set; } = "YDot Administrator";

    /// <summary>Insert the demonstration donors and leads as well as the campaigns.</summary>
    public bool CreateSampleData { get; set; } = true;
}
