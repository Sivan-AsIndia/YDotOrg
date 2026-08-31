using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>Manual mapping for the Languages slice.</summary>
public static class LanguageMappingConfig
{
    /// <summary>
    /// One option in an address or profile form's language picker.
    ///
    /// <paramref name="isPrimaryForCountry"/> and <paramref name="isOfficialInCountry"/> are
    /// passed in rather than read off the language for the reason
    /// <c>TimeZoneMappingConfig.ToGeoLookupResponse</c> sets out: neither is a property OF the
    /// language. Tamil is primary for nothing and official in India and Sri Lanka both, and the
    /// same row has to be able to say so differently depending on which country asked.
    /// </summary>
    public static LanguageLookupResponse ToGeoLookupResponse(
        this Language language, bool isPrimaryForCountry, bool isOfficialInCountry)
    {
        ArgumentNullException.ThrowIfNull(language);

        return new LanguageLookupResponse(
            language.Id,
            language.Code,
            language.CultureCode,
            language.Name,
            language.NativeName,
            language.DisplayLabel,
            language.Iso2,
            language.IsRightToLeft,
            isPrimaryForCountry,
            isOfficialInCountry,
            language.IsDefaultRecommended,
            language.Status,
            language.IsPlatformRow,
            language.SortOrder);
    }

    /// <summary>One option in a Masters-screen picker, in the shared master shape.</summary>
    public static MasterLookupResponse ToLookupResponse(this Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        return new MasterLookupResponse(
            language.Id,
            language.Code,
            language.DisplayLabel,
            language.Status,
            language.IsPlatformRow,
            language.SortOrder);
    }

    /// <summary>
    /// <c>en-IN</c> becomes <c>EN_IN</c>.
    ///
    /// The same fold <c>TimeZoneMappingConfig.ToCode</c> applies to an IANA key, and for the
    /// same reason: the Code and the identifier it derives from would only ever be allowed to
    /// disagree by accident, so it is derived rather than typed twice.
    /// </summary>
    public static string ToCode(string cultureCode)
    {
        ArgumentNullException.ThrowIfNull(cultureCode);

        return cultureCode.Trim().ToUpperInvariant().Replace('-', '_');
    }
}
