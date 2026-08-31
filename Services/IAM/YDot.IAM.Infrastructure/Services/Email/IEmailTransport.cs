namespace YDot.IAM.Infrastructure.Services.Email;

/// <summary>
/// Puts one already-composed message on the wire, and nothing else.
///
/// THIS SEAM EXISTS BECAUSE THE PROVIDER CHANGED AND THE POLICY DID NOT. Whether a message is
/// suppressed, redirected to a single test mailbox, retried or logged is decided once, in
/// <see cref="EmailNotificationService"/>, for every provider. A transport only knows how to
/// hand one message to one relay. Adding a provider therefore cannot quietly lose the
/// redirect guard, which is the one thing standing between a restored production database and
/// a mailbox full of real people.
///
/// IT THROWS <see cref="EmailTransportException"/> AND NOTHING ELSE for a delivery failure, so
/// the retry loop above can catch one type rather than a list of provider-specific ones - the
/// list that silently stopped covering anything the moment the provider stopped being SMTP.
/// Cancellation propagates untouched.
/// </summary>
public interface IEmailTransport
{
    /// <summary>
    /// Delivers the message. Returns the provider's handle on it, or an empty string for a
    /// relay that gives nothing back - which is every plain SMTP relay, including Elastic
    /// Email's.
    /// </summary>
    Task<string> SendAsync(
        string senderAddress,
        string senderName,
        string recipient,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken);
}

/// <summary>
/// A message that could not be delivered. Always wraps the provider's own failure so the
/// original reason survives into the log.
/// </summary>
public sealed class EmailTransportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
