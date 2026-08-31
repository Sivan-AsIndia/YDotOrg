using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// The link between a country and a language spoken in it.
///
/// THE EXACT COUNTERPART OF <see cref="CountryTimeZone"/>, and it exists for the same reason.
/// The relationship is genuinely many-to-many — India has twenty-two scheduled languages,
/// Switzerland four, Canada two — so a single <c>Country.LanguageId</c> column would have been
/// wrong for a large part of the catalogue, and a country-to-language dropdown that offered one
/// answer would be wrong more often than right.
///
/// <see cref="IsPrimary"/> is what a form pre-selects once a country is chosen and nothing more
/// specific has been said. As with the time-zone link, exactly one row per country should carry
/// it, and the configuration indexes for that without enforcing it — a country legitimately
/// passes through a state where none is marked while an administrator edits the set.
///
/// IT IS <see cref="ITenantScoped"/> like everything else in the catalogue: the seeded links are
/// platform rows every Organisation reads, and an Organisation that adds a language of its own
/// can map it to a country without that link becoming visible to anybody else.
/// </summary>
public sealed class CountryLanguage : AuditEntity, ITenantScoped
{
    /// <summary>Null for a platform link. Set to the owning Organisation for a Tenant addition.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Non-null mirror of <see cref="TenantId"/>, maintained by the DbContext.</summary>
    public Guid TenantKey { get; set; }

    public Guid BusinessUnitId { get; set; }

    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public Guid LanguageId { get; set; }

    public Language Language { get; set; } = default!;

    /// <summary>
    /// The language a form pre-selects for this country. For a single-language country it is
    /// simply the only one; for a multi-language country it is the one most widely used in
    /// administration, which is not always the one with the most speakers.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>True where the language has official status in the country.</summary>
    public bool IsOfficial { get; set; }

    /// <summary>Order within the country's language list. Ties fall back to the language's name.</summary>
    public int SortOrder { get; set; }
}
