using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A country in the global master catalogue.
///
/// <see cref="GlobalMasterEntity.Code"/> is the country code the Organisation works with
/// (IN, GB) and <see cref="GlobalMasterEntity.Name"/> is its display name. The ISO columns
/// are kept separately from the Code because a Tenant may legitimately code a country to suit
/// its own reporting while <see cref="Iso2"/> stays the international identifier that
/// integrations and address formatting rely on.
/// </summary>
public class Country : GlobalMasterEntity
{
    /// <summary>The full legal name, for example "Republic of India". Shown on receipts.</summary>
    public string? OfficialName { get; set; }

    public GeographicRegion? Region { get; set; }

    /// <summary>ISO 3166-1 alpha-2, always two upper-case letters. The de facto join key.</summary>
    public string Iso2 { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-3.</summary>
    public string? Iso3 { get; set; }

    /// <summary>ISO 3166-1 numeric, held as text so a leading zero survives.</summary>
    public string? NumericCode { get; set; }

    /// <summary>
    /// The currency a donation defaults to when it is made in this country. A CODE rather
    /// than a foreign key on purpose: a Tenant may add a country before the matching currency
    /// exists, and a hard reference would make the order of setup load-bearing.
    /// </summary>
    public string? DefaultCurrencyCode { get; set; }

    /// <summary>
    /// The country's default currency as a real foreign key.
    ///
    /// <see cref="DefaultCurrencyCode"/> is kept beside it and still carries the ISO alpha-3, so
    /// nothing that reads the code breaks. But a code alone cannot be joined, cannot be checked
    /// on write and quietly tolerates "inr", "INR " and a currency that was never seeded — which
    /// is exactly how a donation form ends up offering a currency the ledger cannot price.
    /// </summary>
    public Guid? DefaultCurrencyId { get; set; }

    public Currency? DefaultCurrency { get; set; }

    /// <summary>
    /// Whether addresses in this country carry a first-level subdivision. Drives whether the
    /// address form shows a State field at all, so Singapore does not ask for one.
    /// </summary>
    public bool HasStates { get; set; }

    /// <summary>Regular expression the postal code is checked against. Null means "do not check".</summary>
    public string? PostalCodePattern { get; set; }

    /// <summary>The dialling prefix, written with its plus sign: +91.</summary>
    public string? PhoneCountryCode { get; set; }

    public ICollection<StateProvince> StateProvinces { get; set; } = [];

    public ICollection<City> Cities { get; set; } = [];

    /// <summary>
    /// Every time zone observed in this country, primary one first.
    ///
    /// A country may legitimately have several — the United States has six — so a time-zone
    /// picker filtered by country shows the whole set rather than guessing at one.
    /// </summary>
    public ICollection<CountryTimeZone> CountryTimeZones { get; set; } = [];

    /// <summary>
    /// Every language spoken in this country, the primary one first.
    ///
    /// Many-to-many for the same reason the zones are — India has twenty-two scheduled
    /// languages and Switzerland four — so a language picker filtered by country shows the
    /// whole set rather than guessing at one.
    /// </summary>
    public ICollection<CountryLanguage> CountryLanguages { get; set; } = [];
}
