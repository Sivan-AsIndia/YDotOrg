namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// Campaign names and tracking-asset resolution, read from the campaign module.
///
/// WHY THIS IS AN ABSTRACTION RATHER THAN A JOIN. Campaigns belong to CAM. PAY holds only a
/// <c>CampaignId</c>, and every donation screen has to show a name beside it - so the choice is
/// between an HTTP call per row, a join across a boundary PAY does not own, or one narrow,
/// read-only lookup behind an interface. The last is what this is: the implementation reads the
/// campaign table it shares a database with, and if campaigns ever move to their own database
/// only the implementation changes.
///
/// EVERY METHOD IS SCOPED BY ORGANISATION. A campaign id arriving on a donation intent came from
/// a request, and resolving it without scoping would let somebody attribute a gift to another
/// charity's campaign - and then see that campaign's name come back, confirming it exists.
/// </summary>
public interface ICampaignDirectory
{
    /// <summary>
    /// Names for a set of campaigns, as a lookup.
    ///
    /// TAKES A SET RATHER THAN ONE ID because the caller is nearly always rendering a page of
    /// donations: twenty rows referencing four campaigns should be one query, not twenty.
    /// Missing ids are simply absent from the result - a campaign deleted after a donation was
    /// recorded must not stop the donation being displayed.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCampaignNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken);

    /// <summary>
    /// One campaign's name, or null if it does not exist in this Organisation.
    /// </summary>
    Task<string?> GetCampaignNameAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the tracking reference from a QR code or link into its attribution.
    ///
    /// SECTION 22 DEPENDS ENTIRELY ON THIS. The reference is the only thing a donor's request
    /// carries, and it is what turns an anonymous gift into one attributed to a campaign, a
    /// channel and the person who shared the link.
    ///
    /// IT IS ALSO HOW THE PUBLIC PATH RESOLVES AN ORGANISATION, which is why it does NOT take a
    /// tenant id: the caller has no session, and the reference itself is what determines which
    /// charity the donation belongs to. The reference is unguessable and unique platform-wide,
    /// so this names a record rather than choosing one.
    /// </summary>
    Task<TrackingAttribution?> ResolveTrackingReferenceAsync(
        string trackingReference, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a campaign is currently able to accept donations.
    ///
    /// A CLOSED OR UNAPPROVED CAMPAIGN MUST NOT TAKE MONEY. Accepting a gift against one leaves
    /// income with nowhere legitimate to be reported, and refunding it afterwards is a worse
    /// experience for the donor than being told at the time.
    /// </summary>
    Task<CampaignDonationEligibility> GetDonationEligibilityAsync(
        Guid tenantId, Guid campaignId, CancellationToken cancellationToken);
}

/// <summary>
/// What a tracking reference resolves to.
///
/// It carries the Organisation because the public donation path has none until this returns.
/// </summary>
public sealed record TrackingAttribution(
    Guid TenantId,
    Guid BusinessUnitId,
    Guid TrackingAssetId,
    Guid CampaignId,
    string CampaignName,
    string? Channel,
    string? Source,
    string? Medium,

    /// <summary>The fundraiser whose link this is, where the asset belongs to a person.</summary>
    Guid? OwnerUserId,

    /// <summary>False once the asset has been retired. A retired link must not take money.</summary>
    bool IsActive);

/// <summary>Whether a campaign may take a donation, and why not when it may not.</summary>
public sealed record CampaignDonationEligibility(
    bool CanAcceptDonations,
    string CampaignName,
    string? CurrencyCode,

    /// <summary>
    /// Donor-facing, deliberately. "This campaign has closed" is something a donor can act on;
    /// "campaign status is PendingApproval" is not.
    /// </summary>
    string? Reason)
{
    public static CampaignDonationEligibility NotFound { get; } =
        new(false, string.Empty, null, "This campaign could not be found.");
}
