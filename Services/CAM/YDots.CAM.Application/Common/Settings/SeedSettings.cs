namespace YDots.CAM.Application.Common.Settings;

/// <summary>
/// What the demonstration campaign is stamped with.
///
/// <see cref="OrganisationId"/> HAS TO MATCH IAM's <c>SeedSettings:SampleOrganisationId</c>, and
/// DON's <c>SeedSettings:OrganisationId</c> with it. Every Tenant-owned row in CAM is filtered to
/// the Organisation on the caller's token, so a campaign stamped with anything else sits outside
/// every real user's scope: present in the database, returned by nothing, and baffling to the
/// first person who goes looking for the campaign the release notes promised.
///
/// THE THREE ARE SET FROM ONE PLACE so they cannot drift. docker-compose passes a single
/// <c>SAMPLE_ORGANISATION_ID</c> into all three services, and the defaults below match the other
/// two - so the platform is correct with no .env at all, and overriding one without the others is
/// the mistake this arrangement exists to prevent.
///
/// WHY CAM SEEDS A CAMPAIGN AT ALL, having deliberately stopped. The seeder's own comment records
/// why the previous sample data was removed: it was stamped with a fabricated Organisation that
/// no real token ever carried. That was the right removal, and it left a gap - a fresh database
/// has no campaign, so the donation form's campaign picker is empty, no tracking asset can be
/// generated, and nothing downstream of a campaign can be demonstrated or tested without somebody
/// first driving the creation wizard by hand. This puts one back, keyed on the Organisation that
/// genuinely exists.
/// </summary>
public sealed class SeedSettings
{
    public const string SectionName = "SeedSettings";

    /// <summary>The seeded Organisation. Matches IAM SeedSettings:SampleOrganisationId.</summary>
    public Guid OrganisationId { get; set; } = Guid.Parse("9fb11890-a08e-4adc-95ca-8e4d71f4dd21");

    /// <summary>The IAM system user, used as the actor and owner on every seeded row.</summary>
    public Guid SystemUserId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Display name written onto the seeded owner field.</summary>
    public string SystemUserDisplayName { get; set; } = "YDot Administrator";

    /// <summary>
    /// Insert the demonstration campaign.
    ///
    /// ON BY DEFAULT, and safe to leave on: the seeder is idempotent by fixed id, so it inserts
    /// once and recognises its own row on every start afterwards. Turn it off for a deployment
    /// that must contain only real campaigns.
    /// </summary>
    public bool CreateSampleData { get; set; } = true;
}
