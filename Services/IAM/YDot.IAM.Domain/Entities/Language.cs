using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A language a person, Organisation or communication can be set to.
///
/// THE SIXTH MASTER, ADDED FOR THE SAME REASON THE OTHER FIVE WERE MIGRATED. Language was the
/// one field on the setup wizard, user creation and lead capture that still had no catalogue
/// behind it: the wizard took free text with a ten-character <c>maxlength</c>, user creation
/// offered four literal <c>&lt;option&gt;</c> tags compiled into the bundle, and lead capture
/// fell back to <c>['English', 'Tamil', 'Hindi', 'Malayalam']</c> when its static JSON had
/// nothing. Three screens, three different vocabularies, none of them the database's.
///
/// <see cref="GlobalMasterEntity.Code"/> holds the culture code with its hyphen folded to an
/// underscore (<c>EN_IN</c>) so it satisfies the platform-wide code format, exactly as
/// <see cref="TimeZoneDefinition"/> does for the IANA key. <see cref="CultureCode"/> keeps the
/// identifier as BCP-47 actually writes it, and that is the value every existing column already
/// stores — <c>Tenant.DefaultCulture</c> and <c>User.PreferredLanguage</c> both hold "en-IN"
/// today, so the catalogue has to speak that string or every stored row stops resolving.
/// </summary>
public sealed class Language : GlobalMasterEntity
{
    /// <summary>
    /// The BCP-47 culture code exactly as written: <c>en-IN</c>, <c>hi-IN</c>, <c>ta-IN</c>.
    ///
    /// This is the value the rest of the platform stores and sends, so it is what a picker's
    /// option value must be. Unique in its scope for the same reason the IANA key is.
    /// </summary>
    public string CultureCode { get; set; } = string.Empty;

    /// <summary>ISO 639-1, the two-letter code: <c>en</c>, <c>hi</c>, <c>ta</c>.</summary>
    public string Iso2 { get; set; } = string.Empty;

    /// <summary>ISO 639-3, the three-letter code: <c>eng</c>, <c>hin</c>, <c>tam</c>.</summary>
    public string? Iso3 { get; set; }

    /// <summary>
    /// The language's name in itself: "हिन्दी" for Hindi, "தமிழ்" for Tamil.
    ///
    /// Shown BESIDE the English name in a picker rather than instead of it. A person choosing
    /// their own language is looking for the word they recognise, and that is rarely the
    /// English one; a person administering somebody else's account needs the English one.
    /// </summary>
    public string? NativeName { get; set; }

    /// <summary>
    /// True for Arabic, Hebrew, Urdu and Farsi.
    ///
    /// Carried on the row rather than derived from a hard-coded list in the browser, because
    /// that list is exactly the kind of thing that gets written once and never updated when a
    /// Tenant adds a language of its own.
    /// </summary>
    public bool IsRightToLeft { get; set; }

    /// <summary>Offered first when a new Organisation, user or lead is set up.</summary>
    public bool IsDefaultRecommended { get; set; }

    /// <summary>The countries this language is spoken in. The other half of <see cref="CountryLanguage"/>.</summary>
    public ICollection<CountryLanguage> CountryLanguages { get; set; } = [];

    /// <summary>
    /// The label a picker shows: "English (India) — English", or just the name where the two
    /// would be the same word twice.
    /// </summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(NativeName) || NativeName.Equals(Name, StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"{Name} — {NativeName}";
}
