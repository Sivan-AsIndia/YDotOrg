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

    /// <summary>
    /// The campaigns an Organisation is currently willing to take donations for.
    ///
    /// WHY THE PUBLIC DONATION FORM NEEDS THIS. Its campaign picker was fed from the campaign
    /// REGISTER, which is authenticated - so a donor who scanned a QR code, the one person the
    /// form exists for, was offered an empty list and told "no eligible campaign or appeal
    /// matches inside your scope". The only campaigns they could give to were ones named by a
    /// tracking reference or a link parameter, which is not a picker.
    ///
    /// IT IS SAFE TO SERVE ANONYMOUSLY, and that is a property of what it returns rather than a
    /// judgement call. Every field here is already public: these are appeals the Organisation is
    /// actively soliciting donations for, printed on posters and shared as links. There is no
    /// target, no raised figure, no owner and no internal state - only what a donor needs to
    /// choose which appeal their gift belongs to.
    ///
    /// THE ORGANISATION IS NOT A PARAMETER THE CALLER CHOOSES. It is resolved from the request's
    /// own host, so a visitor on one charity's donation page cannot list another's appeals.
    ///
    /// APPROVED, SCHEDULED AND ACTIVE ONLY, and inside their own dates. A draft or unapproved
    /// campaign has not been signed off, and one that is paused, closing or closed has been
    /// stopped on purpose - taking money for any of them leaves income with nowhere legitimate to
    /// be reported.
    /// </summary>
    Task<IReadOnlyList<PublicCampaignSummary>> GetDonatableCampaignsAsync(
        Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// One campaign as a donor may see it, and deliberately nothing more.
///
/// NO TARGET AND NO RAISED FIGURE. A donor choosing an appeal does not need them, and how much a
/// charity has raised against a goal is its own business to publish or not.
/// </summary>
public sealed record PublicCampaignSummary(
    Guid Id,
    string Code,
    string Name,
    string? PublicDescription,
    string CurrencyCode);

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
