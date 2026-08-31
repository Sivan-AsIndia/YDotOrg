using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// The link between a country and a time zone observed inside it.
///
/// WHY THIS TABLE EXISTS AT ALL. Until now the only path from a country to a zone ran through
/// <see cref="StateProvince.DefaultTimeZoneId"/>, which meant a form could not offer a zone list
/// until a state had been picked — and a country with no states seeded, or a form that asks for
/// a time zone without asking for an address at all, had no path to one. Worse, the relationship
/// is genuinely MANY-TO-MANY: the United States observes six zones and Australia five, so a
/// single <c>Country.TimeZoneId</c> column would have been wrong for a third of the catalogue.
///
/// <see cref="IsPrimary"/> is what a form pre-selects when the person picks a country and has
/// not yet said anything more specific. Exactly one row per country should carry it, which
/// <c>CountryTimeZoneConfiguration</c> indexes for but does not enforce — a country legitimately
/// passes through a state where none is marked while an administrator is editing the set.
///
/// It is <see cref="ITenantScoped"/> for the same reason the five master tables are: the seeded
/// links are platform rows every Organisation reads, and an Organisation that adds a private
/// country can map its own zones to it without those links becoming visible to anybody else.
/// </summary>
public sealed class CountryTimeZone : AuditEntity, ITenantScoped
{
    /// <summary>Null for a platform link. Set to the owning Organisation for a Tenant addition.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Non-null mirror of <see cref="TenantId"/>, maintained by the DbContext.</summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public Guid TimeZoneId { get; set; }

    public TimeZoneDefinition TimeZone { get; set; } = default!;

    /// <summary>
    /// The zone a form pre-selects for this country. For a single-zone country it is simply the
    /// only one; for a multi-zone country it is the one most of the population lives in.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>Order within the country's zone list. Ties fall back to the zone's UTC offset.</summary>
    public int SortOrder { get; set; }
}
