using YDot.IAM.Domain.Common;

namespace YDot.IAM.Domain.Entities;

/// <summary>
/// A time zone an Organisation, office or campaign schedule can be pinned to.
///
/// NAMED <c>TimeZoneDefinition</c>, NOT <c>TimeZone</c>. <c>System.TimeZone</c> exists in the
/// base class library and is in scope through the implicit usings, so a domain type called
/// <c>TimeZone</c> would shadow it and force a fully-qualified name at every use site - which
/// is exactly the trap the standalone GlobalMaster service fell into.
///
/// <see cref="GlobalMasterEntity.Code"/> holds the IANA key with the slash folded to an
/// underscore (<c>ASIA_KOLKATA</c>) so it satisfies the platform-wide code format, while
/// <see cref="IanaKey"/> keeps the identifier exactly as the tz database writes it.
/// </summary>
public sealed class TimeZoneDefinition : GlobalMasterEntity
{
    /// <summary>The IANA identifier exactly as written: <c>Asia/Kolkata</c>.</summary>
    public string IanaKey { get; set; } = string.Empty;

    /// <summary>The abbreviation shown beside a timestamp: IST, GMT, PST.</summary>
    public string? ShortName { get; set; }

    /// <summary>
    /// Offset from UTC in MINUTES, not hours. India is +330 and Nepal +345 - neither is a
    /// whole number of hours, so an hours column would silently lose them.
    /// </summary>
    public int StandardUtcOffsetMinutes { get; set; }

    public bool SupportsDaylightSaving { get; set; }

    /// <summary>Plain-English note on the local rule, for an operator rather than for code.</summary>
    public string? DaylightSavingRuleNote { get; set; }

    /// <summary>Offered first when a new Organisation or office is set up.</summary>
    public bool IsDefaultRecommended { get; set; }

    public ICollection<StateProvince> StateProvinces { get; set; } = [];

    /// <summary>The countries that observe this zone. The other half of <see cref="CountryTimeZone"/>.</summary>
    public ICollection<CountryTimeZone> CountryTimeZones { get; set; } = [];

    /// <summary>The offset written the way a person reads it: "+05:30".</summary>
    public string OffsetDisplay
    {
        get
        {
            var sign = StandardUtcOffsetMinutes < 0 ? "-" : "+";
            var absolute = Math.Abs(StandardUtcOffsetMinutes);

            return $"{sign}{absolute / 60:00}:{absolute % 60:00}";
        }
    }
}
