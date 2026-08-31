using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Infrastructure.Services.Email;

/// <summary>
/// Hands a message to an SMTP relay.
///
/// THE ONLY TRANSPORT. A relay is all an organisation running this platform on its own
/// infrastructure is likely to have, so <c>EmailSettings.SmtpHost</c> is the whole of the decision -
/// Hostinger's by default, and every provider before it was reached the same way precisely because
/// this stayed SMTP rather than growing a second, HTTPS code path per vendor.
/// This class does not decide policy - whether to send at all, where to redirect and how often to
/// retry live one level up, in <see cref="EmailNotificationService"/>.
///
/// WHY MAILKIT RATHER THAN System.Net.Mail.SmtpClient. The framework client cannot open an
/// implicit-TLS connection at all: its EnableSsl means STARTTLS, so pointing it at port 465 leaves
/// it waiting in the clear for a greeting that never comes, and the send dies on the timeout with
/// nothing useful in the log. Microsoft's own documentation says SmtpClient should not be used for
/// new work. Hostinger, Gmail and most hosted relays all offer implicit TLS, and Hostinger's
/// default port IS 465, so the platform has to speak it. MailKit handles both modes and reports
/// authentication failures distinctly. PAY's sender is this same library for the same reason.
/// </summary>
public sealed class SmtpEmailTransport(IOptions<EmailSettings> emailOptions) : IEmailTransport
{
    private readonly EmailSettings _settings = emailOptions.Value;

    public async Task<string> SendAsync(
        string senderAddress,
        string senderName,
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken)
    {
        // 465 and 2465 negotiate TLS before any SMTP verb; everything else connects in the clear
        // and upgrades. Getting this wrong is a silent stall rather than an error, which is why it
        // is derived from the port instead of left to two settings agreeing with each other.
        var security = _settings.ResolvedUseImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : _settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;

        // The plain-text part is the body and the HTML is the alternative, so a client that
        // strips HTML still shows something readable rather than nothing.
        message.Body = new MultipartAlternative
        {
            new TextPart(TextFormat.Plain) { Text = textBody },
            new TextPart(TextFormat.Html) { Text = htmlBody },
        };

        using var client = new SmtpClient { Timeout = _settings.TimeoutSeconds * 1000 };

        try
        {
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

            // SMTP gives back no durable handle on the message.
            return string.Empty;
        }
        catch (AuthenticationException exception)
        {
            // Called out separately because it is the failure people misread. A relay rejecting
            // the credential looks identical to a wrong host until you read the message.
            throw new EmailTransportException(
                $"SMTP relay {_settings.SmtpHost}:{_settings.SmtpPort} rejected the credentials for "
                + $"{_settings.SmtpUsername}: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is SmtpCommandException
                                              or SmtpProtocolException
                                              or SslHandshakeException
                                              or InvalidOperationException
                                              or FormatException
                                              or IOException
                                              or OperationCanceledException)
        {
            throw new EmailTransportException(
                $"SMTP relay {_settings.SmtpHost}:{_settings.SmtpPort} refused the message "
                + $"({security}): {exception.Message}",
                exception);
        }
    }
}
