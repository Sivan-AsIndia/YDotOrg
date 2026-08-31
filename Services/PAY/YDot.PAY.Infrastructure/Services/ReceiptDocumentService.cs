using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Renders a receipt into a document and delivers it to the donor.
///
/// THE DOCUMENT IS HTML, NOT PDF, and that is a deliberate choice rather than a shortcut. A PDF
/// would mean taking a rendering engine as a dependency - a headless browser, a licensed library
/// or a native binary - and every one of those is a deployment problem that belongs to the
/// installation rather than to this module. The HTML produced here prints to PDF correctly from
/// any browser, is what an e-mail client can show inline, and is small enough to store cheaply
/// for the seven-plus years a tax document has to survive.
///
/// EVERY DONOR-SUPPLIED VALUE IS HTML-ENCODED. A donor's name goes into this document verbatim,
/// the document is stored and later served, and a name containing a script tag would otherwise
/// execute in the browser of whoever opened it - including the finance officer reviewing the
/// receipt register.
///
/// RENDERING AND DELIVERY ARE SEPARATE because they fail independently. A receipt is issued the
/// moment it is numbered and recorded; producing the document and getting it into an inbox are
/// later steps that can fail without making the receipt any less valid. Collapsing them would
/// make a bounced e-mail look like an unissued receipt.
/// </summary>
public sealed class ReceiptDocumentService(
    IOptions<ClientAppSettings> clientSettings,
    IEmailSender emailSender,
    IReceiptDocumentStore documentStore,
    ILogger<ReceiptDocumentService> logger) : IReceiptDocumentService
{
    private readonly ClientAppSettings _clientSettings = clientSettings.Value;

    public async Task<ReceiptDocumentResult> RenderAsync(
        Receipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        try
        {
            var html = BuildHtml(receipt);

            var url = await documentStore.SaveAsync(
                receipt.Id,
                $"receipt-{receipt.ReceiptNumber ?? receipt.Id.ToString("N")}.html",
                Encoding.UTF8.GetBytes(html),
                "text/html; charset=utf-8",
                cancellationToken);

            return new ReceiptDocumentResult(true, url, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not store the rendered document for receipt {ReceiptId}. The receipt "
                + "itself is unaffected and can be re-rendered.",
                receipt.Id);

            return new ReceiptDocumentResult(false, null, "The receipt document could not be stored.");
        }
    }

    /// <summary>
    /// Sends an issued receipt to the donor.
    ///
    /// THE DESTINATION IS PASSED IN rather than read from the receipt, because resending to a
    /// corrected address is a real and necessary operation - and one that is audited by the
    /// caller precisely because sending somebody's tax document elsewhere is exactly the action
    /// that needs justifying later.
    /// </summary>
    public async Task<ReceiptDeliveryResult> DeliverAsync(
        Receipt receipt, string channel, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (!string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            // Honest rather than silently pretending. SMS and post are real channels the model
            // supports, but neither has a provider configured here, and reporting success for a
            // message nobody sent would leave a donor waiting for a receipt that never comes.
            return new ReceiptDeliveryResult(
                false, null, $"Delivery by {channel} is not configured for this installation.");
        }

        var subject = receipt.ReceiptNumber is null
            ? "Your donation receipt"
            : $"Your donation receipt {receipt.ReceiptNumber}";

        var body = BuildHtml(receipt);

        var result = await emailSender.SendAsync(
            destination, subject, body, isHtml: true, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Receipt {ReceiptId} could not be delivered to its donor: {Reason}",
                receipt.Id,
                result.FailureReason);
        }

        return new ReceiptDeliveryResult(result.Succeeded, result.ProviderReference, result.FailureReason);
    }

    /// <summary>
    /// The receipt document itself.
    ///
    /// THE SNAPSHOT COLUMNS ARE USED THROUGHOUT, never the donor's current details. A receipt is
    /// a statement about a moment: if the donor changes their name or address afterwards, the
    /// document they already hold must still match what we can reproduce - and a re-rendered
    /// receipt that disagreed with the one in the donor's hand is exactly what an auditor
    /// queries.
    ///
    /// The styles are inline and the layout is a single column, because this is read in e-mail
    /// clients that strip stylesheets and printed on A4 by people claiming tax relief.
    /// </summary>
    private string BuildHtml(Receipt receipt)
    {
        var builder = new StringBuilder();

        var amount = receipt.Amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
        var issued = receipt.IssuedAtUtc?.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)
                     ?? "Not yet issued";

        builder.Append(
            """
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Donation receipt</title></head>
            <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2430;">
            <div style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e3e6ec;border-radius:8px;padding:32px;">
            """);

        builder.Append(CultureInfo.InvariantCulture, $"""
            <h1 style="margin:0 0 4px;font-size:20px;">Donation receipt</h1>
            <p style="margin:0 0 24px;color:#5a6270;font-size:13px;">
              Receipt {Encode(receipt.ReceiptNumber ?? "(draft)")}
              &nbsp;&middot;&nbsp; Version {receipt.VersionNumber}
              &nbsp;&middot;&nbsp; Financial year {Encode(receipt.FinancialYear)}
            </p>
            """);

        // The amount, given the prominence it has on every receipt anybody has ever received.
        builder.Append(CultureInfo.InvariantCulture, $"""
            <div style="background:#f0f4ff;border:1px solid #d5e0ff;border-radius:6px;padding:20px;margin-bottom:24px;">
              <div style="font-size:12px;text-transform:uppercase;letter-spacing:.06em;color:#5a6270;">Amount received</div>
              <div style="font-size:28px;font-weight:600;margin-top:4px;">{Encode(receipt.Amount.CurrencyCode)} {amount}</div>
              <div style="font-size:13px;color:#5a6270;margin-top:4px;">Issued {Encode(issued)}</div>
            </div>
            """);

        builder.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px;\">");

        AppendRow(builder, "Received from", receipt.DonorName);
        AppendRow(builder, "E-mail", receipt.DonorEmail);
        AppendRow(builder, "Address", receipt.DonorAddress);
        AppendRow(builder, "Tax identifier", receipt.DonorTaxIdentifier);
        AppendRow(builder, "Towards", receipt.CampaignOrFundName);
        AppendRow(builder, "Organisation tax reference", receipt.OrganisationTaxReference);
        AppendRow(builder, "Exemption claimed under", receipt.TaxExemptionReference);

        builder.Append("</table>");

        // A correction has to say what it replaces, on the face of the document. A donor holding
        // both versions must be able to tell which one is current without asking.
        if (receipt.SupersedesReceiptId.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $"""
                <p style="margin:24px 0 0;padding:12px;background:#fff7e6;border:1px solid #ffe0a3;border-radius:6px;font-size:13px;">
                  This receipt replaces an earlier version. {Encode(receipt.CorrectionReason)}
                </p>
                """);
        }

        if (!string.IsNullOrWhiteSpace(_clientSettings.BaseUrl))
        {
            builder.Append(CultureInfo.InvariantCulture, $"""
                <p style="margin:24px 0 0;font-size:13px;color:#5a6270;">
                  You can see all of your donations at
                  <a href="{Encode(_clientSettings.BaseUrl)}" style="color:#2a5bd7;">{Encode(_clientSettings.BaseUrl)}</a>.
                </p>
                """);
        }

        builder.Append("""
            <p style="margin:24px 0 0;font-size:12px;color:#8a919e;">
              This is a computer-generated receipt and does not require a signature.
            </p>
            </div></body></html>
            """);

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"""
            <tr>
              <td style="padding:8px 0;color:#5a6270;width:200px;vertical-align:top;">{Encode(label)}</td>
              <td style="padding:8px 0;vertical-align:top;">{Encode(value)}</td>
            </tr>
            """);
    }

    /// <summary>
    /// HTML-encodes a donor-supplied value. See the class comment for why this is not optional.
    /// </summary>
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
