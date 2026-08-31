using YDots.CAM.Domain.Entities;

namespace YDots.CAM.Application.Common.Abstractions.Services;

/// <summary>
/// Produces the attribution key a tracking asset carries, and the URL that key resolves
/// through.
///
/// WHY THIS IS A SERVICE AND NOT A LINE IN THE HANDLER. The reference is the value a donation
/// intent carries back from the public flow, and it is how a gift is credited to a campaign,
/// channel, source and medium. Two assets sharing a reference means two campaigns claiming the
/// same money, so the generation has to be unguessable, collision-resistant and written down in
/// one place rather than reinvented at each call site.
///
/// The generated URL is built on <c>CampaignSettings.TrackingBaseUrl</c>, which is
/// configuration for a reason: a QR code printed with a staging host is not recoverable.
/// </summary>
public interface ITrackingReferenceGenerator
{
    /// <summary>
    /// A new attribution reference.
    ///
    /// Derived from random bytes rather than from a counter or the asset id. A sequential
    /// reference would let anybody holding one enumerate every other campaign's assets, and an
    /// id-derived one would leak how many exist.
    /// </summary>
    string NewReference();

    /// <summary>
    /// The destination URL, with the reference and the UTM parameters attached.
    ///
    /// Returns null when no tracking base URL is configured, so the asset can still be created
    /// and the URL filled in once the environment is set up - rather than the whole create call
    /// failing over a value that has nothing to do with the operator's request.
    /// </summary>
    string? BuildUrl(TrackingAsset asset, string sourceCode, string mediumCode, string campaignCode);
}
