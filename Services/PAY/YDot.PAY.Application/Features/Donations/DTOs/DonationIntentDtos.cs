using YDot.PAY.Application.Common.Models;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Donations.DTOs;

// =====================================================================================
// Public donation initiation - sections 11, 19 to 22
// =====================================================================================

/// <summary>
/// Starting a donation. The one request every entry channel produces.
///
/// SECTION 22 IS WHY THIS IS ONE TYPE AND NOT NINE. A QR scan, a website button, an e-mail link
/// and a fundraiser's lead link all differ ONLY in their attribution, so they differ only in the
/// values of <see cref="SourceType"/>, <see cref="TrackingReference"/> and
/// <see cref="LeadReference"/> - never in the shape of the request or the decision that follows.
///
/// IT CARRIES NO OrganisationId. The Organisation is resolved from the tracking reference, the
/// campaign or the public organisation slug in the route - never from a field the caller can
/// set, or anybody could create donations against any charity on the platform.
/// </summary>
public sealed record CreateDonationIntentRequest(
    string DonorName,
    string Email,
    decimal Amount,
    string CurrencyCode,
    string? Mobile = null,
    Guid? CampaignId = null,

    /// <summary>
    /// The tracking reference from the QR code or link the donor followed.
    ///
    /// Resolves to a CAM tracking asset, and through it to the campaign, channel, source and
    /// medium - which is what makes the gift attributable afterwards.
    /// </summary>
    string? TrackingReference = null,

    DonationSourceType SourceType = DonationSourceType.DirectLink,

    /// <summary>The lead this came from, where a fundraiser captured the donor first.</summary>
    Guid? LeadReference = null,

    string? TaxIdentifier = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    Guid? CountryId = null,
    Guid? StateId = null,
    Guid? CityId = null,
    string? PostalCode = null,

    /// <summary>Section 11: consent is captured before the intent is created, not after.</summary>
    bool ConsentGiven = false,

    string? ConsentVersion = null,
    bool AllowPublicRecognition = false,
    string? PublicRecognitionName = null);

/// <summary>
/// What the donor gets back after creating an intent.
///
/// <see cref="ExistingDonorMatched"/> IS THE FIELD THE CLIENT BRANCHES ON, and it is the whole
/// point of sections 12 to 14: true sends the donor to sign in with the intent preserved, false
/// lets them continue straight to payment without creating a password first.
/// </summary>
public sealed record DonationIntentResponse(
    Guid Id,
    string IntentReference,
    DonationIntentStatus Status,
    string StatusDescription,
    MoneyResponse Amount,
    string DonorName,
    string Email,
    string? Mobile,
    Guid? CampaignId,
    string? CampaignName,
    DonationSourceType SourceType,
    string? TrackingReference,

    /// <summary>
    /// Section 12: is this e-mail already a donor for THIS organisation?
    ///
    /// Null means the check has not run. True means sign in first; false means continue.
    /// </summary>
    bool? ExistingDonorMatched,

    string? PaymentLinkUrl,
    DateTimeOffset? PaymentLinkExpiresAtUtc,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    long Version,

    /// <summary>What the donor or the operator may do next, decided by the server.</summary>
    IReadOnlyList<string> PermittedActions);

/// <summary>
/// The answer to section 12's existing-donor check, on its own.
///
/// A SEPARATE ENDPOINT AND RESPONSE, because the client asks the question BEFORE committing to
/// an intent - the donor types their e-mail and the form needs to know immediately whether to
/// show a sign-in prompt or carry on.
/// </summary>
public sealed record ExistingDonorCheckResponse(
    bool ExistingDonorFound,

    /// <summary>Masked, always. "jo***@example.org" confirms recognition without confirming the address.</summary>
    string? MaskedEmail,

    /// <summary>True when the donor already has an account they can sign in with.</summary>
    bool HasActiveAccount,

    /// <summary>What the client should do next: SignIn or Continue.</summary>
    string NextStep,

    string Message);

/// <summary>Asking for a payment link on an intent.</summary>
public sealed record CreatePaymentLinkRequest(long ExpectedVersion, string? PreferredMethod = null);

/// <summary>Where to send the donor, and how long it lasts.</summary>
public sealed record PaymentLinkResponse(
    Guid IntentId,
    string IntentReference,
    string PaymentLinkUrl,
    DateTimeOffset ExpiresAtUtc,
    MoneyResponse Amount,
    string GatewayName,
    int AttemptNumber);

/// <summary>Cancelling an intent.</summary>
public sealed record CancelDonationIntentRequest(long ExpectedVersion, string Reason);

/// <summary>One row of the donation intent register.</summary>
public sealed record DonationIntentListItemResponse(
    Guid Id,
    string IntentReference,
    string DonorName,

    /// <summary>Masked unless the caller holds pay.donations.view-sensitive-donor.</summary>
    string Email,

    MoneyResponse Amount,
    DonationIntentStatus Status,
    string StatusDescription,
    DonationSourceType SourceType,
    Guid? CampaignId,
    string? CampaignName,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    bool? ExistingDonorMatched,
    DateTimeOffset CreatedAtUtc,
    long Version);

/// <summary>The intent detail screen - SCR-PAY-001.</summary>
public sealed record DonationIntentDetailResponse(
    Guid Id,
    Guid TenantId,
    string IntentReference,
    DonationIntentStatus Status,
    string StatusDescription,
    MoneyResponse Amount,
    string DonorName,
    string Email,
    string? Mobile,
    string? TaxIdentifier,
    string? AddressLine1,
    string? AddressLine2,
    Guid? CountryId,
    Guid? StateId,
    Guid? CityId,
    string? PostalCode,
    Guid? CampaignId,
    string? CampaignName,
    DonationSourceType SourceType,
    string SourceDescription,
    string? TrackingReference,
    Guid? TrackingAssetId,
    Guid? LeadId,
    Guid? DonorId,
    bool ConsentGiven,
    string? ConsentVersion,
    DateTimeOffset? ConsentGivenAtUtc,
    bool AllowPublicRecognition,
    string? PublicRecognitionName,
    string? PaymentLinkUrl,
    DateTimeOffset? PaymentLinkExpiresAtUtc,
    bool? ExistingDonorMatched,
    DateTimeOffset? ExistingDonorCheckedAtUtc,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    string? FailureReason,
    string? CancellationReason,

    /// <summary>Section 24: the lifecycle history the intent has to retain.</summary>
    IReadOnlyList<PaymentAttemptResponse> Attempts,

    /// <summary>The donation, once one exists. Null while the intent is unpaid.</summary>
    DonationSummaryResponse? Donation,

    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>One attempt, as the support timeline shows it.</summary>
public sealed record PaymentAttemptResponse(
    Guid Id,
    int AttemptNumber,
    PaymentAttemptStatus Status,
    string StatusDescription,
    string GatewayName,
    string? GatewayReference,
    PaymentMethodType? MethodType,
    string? MaskedInstrument,
    MoneyResponse RequestedAmount,
    MoneyResponse? CapturedAmount,
    DateTimeOffset InitiatedAtUtc,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? GatewayResultCode,

    /// <summary>
    /// What to show the donor.
    ///
    /// DELIBERATELY NOT THE GATEWAY'S OWN MESSAGE, which often names the issuing bank's decline
    /// reason - something the donor cannot act on and sometimes should not see on a charity's
    /// website.
    /// </summary>
    string? DonorFacingMessage,

    /// <summary>True when the outcome is unknown and must be verified rather than retried.</summary>
    bool NeedsVerification);

/// <summary>Filter for the intent register.</summary>
public sealed class DonationIntentSearchFilter : PaginationRequest
{
    public DonationIntentStatus? Status { get; set; }

    public DonationSourceType? SourceType { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid? LeadId { get; set; }

    public DateTimeOffset? CreatedFromUtc { get; set; }

    public DateTimeOffset? CreatedToUtc { get; set; }

    /// <summary>
    /// Intents that failed and have not been retried.
    ///
    /// The queue Payment Support works from - section 23. A plain Status filter would not
    /// separate "failed once and abandoned" from "failed and already retried successfully".
    /// </summary>
    public bool? NeedsAttention { get; set; }
}
