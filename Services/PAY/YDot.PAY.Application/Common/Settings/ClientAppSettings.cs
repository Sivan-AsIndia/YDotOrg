namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// Where the Angular client lives, and where the donor is sent back to after paying.
///
/// A WILDCARD CORS ORIGIN IS NOT AN OPTION. The staff client sends its bearer token, and a
/// browser refuses to send credentials to a wildcard origin - so the list must be explicit.
/// </summary>
public sealed class ClientAppSettings
{
    public const string SectionName = "ClientAppSettings";

    public IList<string> AllowedOrigins { get; set; } = [];

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where the gateway returns the donor to after payment.
    ///
    /// The intent reference is appended, so the result page can show the right donation without
    /// the donor needing an account.
    /// </summary>
    public string PaymentResultPath { get; set; } = "/give/result";

    /// <summary>Where the donor lands to activate the account created for them. Section 17.</summary>
    public string DonorActivationPath { get; set; } = "/auth/activate";

    /// <summary>
    /// The screen a receipt links back to: Payments and Receipts.
    ///
    /// IT USED TO BE THE BARE BASE URL, which is the front door of the platform rather than a
    /// place to look at a donation. A receipt that says "you can see all of your donations at
    /// http://localhost:6700" lands the reader on whatever that host serves first - a sign-in
    /// screen, a landing page - and they still have to find the register themselves.
    ///
    /// Configured rather than written into the document, so renaming the route is an edit here
    /// instead of a link that silently 404s in every receipt already in a donor's inbox.
    /// </summary>
    public string ReceiptsPath { get; set; } = "/app/donations/payment-event-queue";
}
