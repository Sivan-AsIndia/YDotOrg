using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Donations.Commands.ManageIntent;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Queries;
using YDot.PAY.Application.Features.Payments.Commands.ProcessPayment;
using YDot.PAY.Application.Features.Payments.DTOs;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The donor-facing donation flow - sections 11 to 14 and 19 to 23.
///
/// EVERY ACTION HERE IS <c>[AllowAnonymous]</c>, and that is the defining fact about this
/// controller. The donor is a stranger with a QR code, a link in an e-mail or a button on a web
/// page; they have no account, no token and no session. Requiring one would mean asking somebody
/// to register before they are allowed to give money, which is the single most reliable way to
/// lose a donation.
///
/// SO WHAT PROTECTS IT? Four things, and it is worth being explicit because "anonymous" and
/// "unprotected" are not the same word:
///
///   1. THE ORGANISATION IS NEVER TAKEN FROM THE BODY. It is resolved by the tenant middleware
///      from the intent reference in the route, or from the tracking reference the donor
///      followed. A caller cannot name a charity to donate against.
///   2. EVERY REFERENCE IS UNGUESSABLE - twelve characters from a 31-character alphabet, about
///      59 bits. Reaching somebody else's donation means guessing one, not incrementing one.
///   3. NOTHING HERE RETURNS UNMASKED DONOR DETAIL. The result page shows a masked e-mail even
///      to the donor themselves, because the endpoint is reachable by anybody holding the link.
///   4. THE PERMITTED ACTIONS COME BACK EMPTY OF STAFF ACTIONS, because they are computed with a
///      permission probe that answers false to everything.
///
/// THE STAFF EQUIVALENTS OF THESE ROUTES LIVE IN <see cref="DonationIntentsController"/> and
/// require both a token and a permission. Two controllers rather than one set of endpoints with
/// a conditional: an endpoint that is anonymous under some conditions is one whose security is
/// decided by a branch somebody will eventually get wrong.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/donations")]
[Produces("application/json")]
public sealed class PublicDonationsController(
    DonationIntentCommandHandler intents,
    DonationQueryHandler queries,
    PaymentProcessingCommandHandler payments) : ApiControllerBase
{
    /// <summary>
    /// Starts a donation. Section 11 and section 22's nine entry channels.
    ///
    /// ONE ENDPOINT FOR EVERY CHANNEL. A QR scan, a website button, an e-mail link and a
    /// fundraiser's lead link differ only in their attribution - the SourceType, the tracking
    /// reference and the lead - so they differ only in the values on this request, never in the
    /// shape of it or in the decision that follows.
    /// </summary>
    [HttpPost("initiate")]
    [ProducesResponseType(typeof(ApiResponse<DonationIntentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateAsync(
        [FromBody] CreateDonationIntentRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(new CreateDonationIntentCommand(request), cancellationToken),
            "Donation started.");

    /// <summary>
    /// Section 12: is this donor already known to this charity?
    ///
    /// A BRANCH, NOT AN ERROR. A match sends the donor to sign in with the intent preserved
    /// (section 13); no match lets them continue straight to payment without creating a password
    /// first (section 14).
    ///
    /// IT TAKES THE INTENT REFERENCE RATHER THAN AN E-MAIL, deliberately. An endpoint that
    /// accepted a bare address would be an oracle: type an address, learn whether that person
    /// gives to this charity. Working from a reference the caller already holds means they can
    /// only ask about a donation they started.
    /// </summary>
    [HttpPost("{intentReference}/check-donor")]
    [ProducesResponseType(typeof(ApiResponse<ExistingDonorCheckResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckDonorAsync(
        string intentReference, CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(new CheckExistingDonorCommand(intentReference), cancellationToken));

    /// <summary>
    /// Issues the payment link and opens an attempt.
    ///
    /// THE EXPECTED VERSION IS ON THE BODY because two tabs, or a double-clicked button, would
    /// otherwise open two attempts against one intent - two links, and a donor who pays both has
    /// given twice.
    /// </summary>
    [HttpPost("{intentReference}/payment-link")]
    [ProducesResponseType(typeof(ApiResponse<PaymentLinkResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePaymentLinkAsync(
        string intentReference,
        [FromBody] CreatePaymentLinkRequest request,
        CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(
                new CreatePaymentLinkCommand(intentReference, request), cancellationToken));

    /// <summary>
    /// Opens the provider's checkout for this donation, so the donor pays without leaving us.
    ///
    /// THIS IS THE ROUTE SUBMIT TAKES. The donor pressed a button on a form they are looking at;
    /// the payment form should open in front of them. The payment-link endpoint above stays for
    /// what a link is genuinely for - reaching somebody who is NOT at a screen - and is also what
    /// the client falls back to when the organisation's provider cannot draw an in-page checkout.
    ///
    /// WHAT COMES BACK CARRIES NO SECRET. An order id, the merchant's publishable key, the amount
    /// for display and the donor's own details to prefill. The price is the order's, held by the
    /// provider, so a browser that edits this response changes a label and nothing else.
    /// </summary>
    [HttpPost("{intentReference}/checkout-session")]
    [ProducesResponseType(typeof(ApiResponse<CheckoutSessionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCheckoutSessionAsync(
        string intentReference,
        [FromBody] CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(
            await intents.HandleAsync(
                new CreateCheckoutSessionCommand(intentReference, request), cancellationToken));

    /// <summary>
    /// Takes the signed result of a finished checkout and settles the donation.
    ///
    /// ANONYMOUS, LIKE EVERYTHING ELSE HERE, AND SAFE FOR THE SAME REASON THE OTHERS ARE - with
    /// one addition that matters more here than anywhere. The body is a claim made by a browser
    /// that a payment happened, which is exactly the claim an attacker would like to make. It is
    /// believed only because it carries a signature made with the merchant secret, checked on the
    /// server before anything is written. Without that this endpoint would let anybody mark any
    /// donation paid by naming its reference.
    ///
    /// IT DOES NOT DECIDE THE OUTCOME EITHER. A good signature earns the payment id the right to
    /// be recorded; whether money actually moved is then asked of the provider directly, through
    /// the same verification the result page and Support and Retry use.
    /// </summary>
    [HttpPost("{intentReference}/checkout-confirm")]
    [ProducesResponseType(typeof(ApiResponse<PaymentVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmCheckoutAsync(
        string intentReference,
        [FromBody] ConfirmCheckoutRequest request,
        CancellationToken cancellationToken) =>
        FromResult(
            await payments.HandleAsync(
                new ConfirmCheckoutPaymentCommand(intentReference, request), cancellationToken));

    /// <summary>
    /// The donor's own view of their donation, for the result page they land on after paying.
    ///
    /// ALWAYS MASKED, even though it is the donor's own record. There is no session to prove who
    /// is holding the link, so the safe branch is the only branch.
    /// </summary>
    [HttpGet("{intentReference}")]
    [ProducesResponseType(typeof(ApiResponse<DonationIntentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        string intentReference, CancellationToken cancellationToken) =>
        FromResult(
            await queries.HandleAsync(
                new GetDonationIntentByReferenceQuery(intentReference), cancellationToken));

    /// <summary>
    /// Asks the gateway what actually happened - SCR-PAY-002.
    ///
    /// THE DONOR CAN CALL THIS THEMSELVES, which is the point of the verification screen: a donor
    /// whose browser was closed mid-payment, or who saw a spinner and nothing else, needs a way to
    /// find out whether their money left their account. Without it the only recourse is a support
    /// call, and the support agent asks the gateway the same question.
    ///
    /// IT NEVER RETRIES. Verification asks; it does not pay. A retry disguised as a check is how a
    /// donor gets charged twice.
    /// </summary>
    [HttpPost("{intentReference}/verify")]
    [ProducesResponseType(typeof(ApiResponse<PaymentVerificationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyAsync(
        string intentReference, CancellationToken cancellationToken) =>
        FromResult(
            await payments.HandleAsync(
                new VerifyPaymentCommand(new VerifyPaymentRequest(IntentReference: intentReference)),
                cancellationToken));
}
