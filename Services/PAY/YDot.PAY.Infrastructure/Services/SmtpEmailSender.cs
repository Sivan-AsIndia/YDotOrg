using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using YDot.PAY.Application.Common.Settings;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Sends one message. The narrowest interface that covers what this module actually needs.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        string to, string subject, string body, bool isHtml, CancellationToken cancellationToken);
}

/// <summary>Whether the message went, and the provider's handle on it if it did.</summary>
public sealed record EmailSendResult(bool Succeeded, string? ProviderReference, string? FailureReason);

/// <summary>
/// Sends receipt mail over SMTP.
///
/// NOTHING HERE EVER THROWS, which is the most important decision in this file. By the time a
/// receipt is being delivered the donation is captured, the receipt is numbered and the books are
/// correct; an SMTP relay that is down must not undo any of that. Every failure comes back as a
/// result, is recorded as a delivery attempt against the receipt, and appears in the undelivered
/// queue for somebody to work.
///
/// WITH <c>Enabled</c> OFF the message is logged instead of sent - enough to exercise the whole
/// flow with no mail credentials at all.
///
/// <c>RedirectAllToAddress</c> SENDS EVERYTHING TO ONE MAILBOX with the intended recipient in the
/// subject. In a module that mails tax documents to real donors, that switch is what stops a
/// restored production database from doing so out of a test environment.
///
/// WHY MAILKIT RATHER THAN System.Net.Mail.SmtpClient, which this used to be. The framework client
/// cannot open an implicit-TLS connection at all - its EnableSsl means STARTTLS - so on port 465
/// it connected in the clear and waited for a greeting that never came. Against
/// smtp.hostinger.com that was a twenty-second stall ending in "The operation has timed out",
/// while IAM's MailKit transport sent on the same port, with the same credentials, in three
/// seconds. Because ONE SET OF ENVIRONMENT VARIABLES CONFIGURES BOTH SERVICES, that asymmetry was
/// never a PAY bug in isolation: pointing the platform at a 465 relay left invitations arriving
/// and every donation receipt in the undelivered queue. Microsoft's own documentation says
/// SmtpClient should not be used for new work. This file is now the same transport as IAM's.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<EmailSettings> emailSettings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailSettings _settings = emailSettings.Value;

    public async Task<EmailSendResult> SendAsync(
        string to, string subject, string body, bool isHtml, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var reference = $"MAIL-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.SmtpHost))
        {
            logger.LogInformation(
                "Mail is disabled. Would have sent {Reference} to {Recipient} with subject {Subject}.",
                reference,
                to,
                subject);

            // REPORTED AS A SUCCESS, deliberately. With mail switched off the intended behaviour
            // is that the flow completes; reporting a failure would fill the undelivered queue
            // with receipts nobody ever meant to send.
            return new EmailSendResult(true, reference, null);
        }

        var recipient = string.IsNullOrWhiteSpace(_settings.RedirectAllToAddress)
            ? to
            : _settings.RedirectAllToAddress;

        var effectiveSubject = string.IsNullOrWhiteSpace(_settings.RedirectAllToAddress)
            ? subject
            : $"[to: {to}] {subject}";

        // NO SENDER, NO SEND. EmailSettings.ResolvedSenderAddress falls back to the authenticated
        // SMTP account only when that is itself an address, which is not true of every relay.
        // Without this the receipt fails on a parse error and lands in the undelivered queue
        // blamed on a bad address, when what is actually missing is EmailSettings:SenderAddress.
        // Reported as a failure rather than swallowed like the disabled case: mail IS switched on
        // here, so this receipt genuinely did not go and belongs in the queue - with a reason
        // somebody can act on.
        if (!_settings.IsSenderConfigured)
        {
            logger.LogError(
                "Cannot send {Reference} to {Recipient}: no sender address is configured. Set "
                + "EmailSettings:SenderAddress to an address the relay at {Relay} will send as - "
                + "it cannot be inferred from the SMTP username {Username}.",
                reference,
                recipient,
                _settings.SmtpHost,
                _settings.SmtpUsername);

            return new EmailSendResult(
                false, null, "No sender address is configured (EmailSettings:SenderAddress).");
        }

        // 465 and 2465 negotiate TLS before any SMTP verb; everything else connects in the clear
        // and upgrades. Derived from the port rather than left to two settings agreeing with each
        // other, because the failure when they disagree is a silent stall, not an error - which is
        // the exact failure that made this file MailKit.
        var security = _settings.ResolvedUseImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : _settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        using var client = new SmtpClient { Timeout = _settings.TimeoutSeconds * 1000 };

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.ResolvedSenderAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = effectiveSubject;

            // A receipt is worth sending in both forms: a client that strips HTML still shows
            // something readable rather than an empty message where a tax document should be.
            message.Body = isHtml
                ? new MultipartAlternative
                {
                    new TextPart(TextFormat.Plain) { Text = ToPlainText(body) },
                    new TextPart(TextFormat.Html) { Text = body },
                }
                : new TextPart(TextFormat.Plain) { Text = body };

            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, security, cancellationToken);

            // Only authenticate when there is something to authenticate with: an internal relay
            // that accepts mail from inside the network has no credentials, and offering empty
            // ones is refused outright.
            if (!string.IsNullOrWhiteSpace(_settings.SmtpUsername))
            {
                await client.AuthenticateAsync(
                    _settings.SmtpUsername, _settings.SmtpPassword, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            logger.LogInformation("Sent {Reference} to {Recipient}.", reference, recipient);

            return new EmailSendResult(true, reference, null);
        }
        catch (AuthenticationException exception)
        {
            // Called out separately because it is the failure people misread: a relay rejecting
            // the credential looks identical to a wrong host until you read the message.
            logger.LogError(
                exception,
                "Relay {Relay}:{Port} rejected the credentials for {Username} sending {Reference}. "
                + "The receipt is unaffected and will appear in the undelivered queue.",
                _settings.SmtpHost,
                _settings.SmtpPort,
                _settings.SmtpUsername,
                reference);

            return new EmailSendResult(
                false, null, $"The relay rejected the credentials: {exception.Message}");
        }
        catch (Exception exception) when (exception is SmtpCommandException
                                              or SmtpProtocolException
                                              or SslHandshakeException
                                              or InvalidOperationException
                                              or FormatException
                                              or ArgumentException
                                              or IOException
                                              or OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not send {Reference} to {Recipient} via {Relay}:{Port} ({Security}). The "
                + "receipt is unaffected and will appear in the undelivered queue.",
                reference,
                recipient,
                _settings.SmtpHost,
                _settings.SmtpPort,
                security);

            return new EmailSendResult(false, null, exception.Message);
        }
    }

    /// <summary>
    /// A readable plain-text fallback for an HTML receipt.
    ///
    /// Deliberately crude - it turns the block-level tags the receipt template emits into line
    /// breaks, strips the rest and unescapes the entities. It is the alternative part of a
    /// multipart message, not the one anybody is expected to read, and a real HTML-to-text
    /// conversion would be a dependency earning its place on a body this module generates itself.
    /// </summary>
    private static string ToPlainText(string html)
    {
        var withBreaks = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<\s*(br|/p|/div|/tr|/h[1-6])\s*/?>",
            "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var stripped = System.Text.RegularExpressions.Regex.Replace(withBreaks, "<[^>]+>", string.Empty);

        return System.Net.WebUtility.HtmlDecode(stripped).Trim();
    }
}
