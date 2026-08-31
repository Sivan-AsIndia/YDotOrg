using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Application.Common.Settings;
using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// Produces the attribution key a tracking asset carries, and the URL it resolves through.
///
/// THE REFERENCE COMES FROM <see cref="RandomNumberGenerator"/>, NOT FROM <c>Random</c> OR A
/// COUNTER. It is the value that credits money to a campaign, so two properties matter: it must
/// not be guessable, or somebody could fabricate attribution; and it must not be enumerable, or
/// anybody holding one reference could walk every other campaign's assets.
///
/// THE ALPHABET EXCLUDES THE AMBIGUOUS CHARACTERS. A reference is printed under QR codes and
/// read aloud down phone lines, and 0/O and 1/I/l are where that goes wrong.
/// </summary>
public sealed class TrackingReferenceGenerator(IOptions<CampaignSettings> settings)
    : ITrackingReferenceGenerator
{
    /// <summary>Crockford-style alphabet: no 0, O, 1, I or L.</summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>
    /// Twelve characters of a 31-character alphabet is about 59 bits - far beyond any realistic
    /// collision risk, and short enough to print legibly under a QR code.
    /// </summary>
    private const int ReferenceLength = 12;

    private readonly CampaignSettings _settings = settings.Value;

    public string NewReference()
    {
        var characters = new char[ReferenceLength];

        for (var index = 0; index < ReferenceLength; index++)
        {
            // RandomNumberGenerator.GetInt32 is uniform over the range. Taking a byte modulo the
            // alphabet length would NOT be: 256 is not a multiple of 31, so the first few
            // characters would come up slightly more often.
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }

    /// <summary>
    /// Builds the destination URL with the reference and UTM parameters attached.
    ///
    /// THE ASSET DESTINATION IS THE BASE where it is an absolute URL, and the configured
    /// tracking host is used otherwise. That ordering matters: a UTM link should point at the
    /// campaign's own landing page with parameters appended, while a QR code or short link
    /// points at our redirector, which resolves the reference and then forwards.
    /// </summary>
    public string? BuildUrl(TrackingAsset asset, string sourceCode, string mediumCode, string campaignCode)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (string.IsNullOrWhiteSpace(asset.TrackingReference))
        {
            return null;
        }

        var baseUrl = ResolveBaseUrl(asset);

        if (baseUrl is null)
        {
            // No tracking host configured and no absolute destination. The asset is still
            // perfectly valid; the URL is filled in once the environment is set up, rather than
            // failing a create call over a value that has nothing to do with the request.
            return null;
        }

        var builder = new UriBuilder(baseUrl);

        var parameters = new List<string>
        {
            "utm_campaign=" + Uri.EscapeDataString(campaignCode),
            "utm_source=" + Uri.EscapeDataString(sourceCode),
            "utm_medium=" + Uri.EscapeDataString(mediumCode),
            "ref=" + Uri.EscapeDataString(asset.TrackingReference)
        };

        if (!string.IsNullOrWhiteSpace(asset.ContentTag))
        {
            parameters.Add("utm_content=" + Uri.EscapeDataString(asset.ContentTag));
        }

        // Appended rather than replaced, so a destination that already carries its own query
        // string keeps it.
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? string.Join('&', parameters)
            : builder.Query.TrimStart('?') + "&" + string.Join('&', parameters);

        return builder.Uri.ToString();
    }

    private Uri? ResolveBaseUrl(TrackingAsset asset)
    {
        if (Uri.TryCreate(asset.Destination, UriKind.Absolute, out var destination)
            && (destination.Scheme == Uri.UriSchemeHttp || destination.Scheme == Uri.UriSchemeHttps))
        {
            return destination;
        }

        return Uri.TryCreate(_settings.TrackingBaseUrl, UriKind.Absolute, out var configured)
            ? configured
            : null;
    }
}
