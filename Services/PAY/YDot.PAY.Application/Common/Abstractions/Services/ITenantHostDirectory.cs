namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// The host name an Organisation is reached on, read from the identity module.
///
/// WHY PAY NEEDS ONE AT ALL. A receipt is a document somebody reads later, on their own, with no
/// session and no context but what the page gives them - so the link on it has to land them on
/// THEIR charity's site. The Organisation is resolved FROM THE HOST (see
/// <c>TenantResolutionMiddleware</c>), so a link built on the platform host resolves the wrong
/// Organisation or none, and the reader is told the donation does not exist.
///
/// READ-ONLY, AND OVER THE SHARED DATABASE, exactly like <see cref="ICampaignDirectory"/>. PAY
/// owns no copy of the domain list and must not invent one: two places recording which host
/// belongs to which charity is one place too many when the answer decides where a donor lands.
/// </summary>
public interface ITenantHostDirectory
{
    /// <summary>
    /// The host to build links to this Organisation on, or null when it has none registered.
    ///
    /// NULL IS A NORMAL ANSWER, not a failure. A brand-new Organisation may have no verified host
    /// yet, and a receipt still has to render - the caller falls back to the platform host rather
    /// than refusing to produce the document.
    /// </summary>
    Task<string?> GetPrimaryHostAsync(Guid tenantId, CancellationToken cancellationToken);
}
