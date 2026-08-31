namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Outbound mail, bound from the EmailSettings section.
///
/// THESE CREDENTIALS LIVE ON THE SERVER, DELIBERATELY. They used to sit in the Angular
/// environment file, where everything is compiled into the bundle every visitor downloads
/// and could be read from the browser dev tools. The UI now never sends mail: it calls an
/// endpoint and the API does the rest.
///
/// ONE TRANSPORT: AN SMTP RELAY, and by default Hostinger's. Three providers came before it and
/// all are gone. A Google App Password was tied to one person's personal account - it died with
/// their password change, it sent as them whatever the branding said, and Google throttled a batch
/// of invitations hard enough to lose some. Elastic Email replaced it, then Resend, and now a
/// Hostinger mailbox on the organisation's own domain. Every one of those moves changed
/// <see cref="SmtpHost"/> and a credential and nothing else, which is the entire point of the
/// relay staying plain SMTP.
///
/// HOSTINGER AUTHENTICATES WITH THE FULL MAILBOX ADDRESS, and sends only as that mailbox or one of
/// its aliases. That makes <see cref="SmtpUsername"/> an e-mail address again, so the "leave the
/// sender blank and send as the authenticated account" fallback in
/// <see cref="ResolvedSenderAddress"/> works here. It did not under Resend, which authenticates as
/// the literal string "resend", and the guard that made that safe is still in place.
///
/// PORT 465, WHICH IS IMPLICIT TLS - the handshake completes before a single SMTP verb is spoken.
/// That is a different thing from STARTTLS on 587, where the connection opens in the clear and
/// upgrades; both work against Hostinger and the transport picks the mode from the port. 25 is
/// closed by Hostinger and by most networks, so it is not an option. 465 is also the port that
/// forced PAY's sender onto MailKit: System.Net.Mail cannot open an implicit-TLS connection at
/// all, and one set of environment variables configures both services.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "EmailSettings";

    /// <summary>
    /// When false nothing is sent and every message is written to the log instead. That is
    /// enough to walk the whole invite to activate to sign-in flow with no credentials at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // ---- SMTP --------------------------------------------------------------------------------

    public string SmtpHost { get; set; } = "smtp.hostinger.com";

    /// <summary>
    /// 465, implicit TLS. 587 is the same relay over STARTTLS and works equally well;
    /// <see cref="ResolvedUseImplicitTls"/> picks the handshake from this number, so switching is
    /// a one-line change with no second flag to keep in step.
    /// </summary>
    public int SmtpPort { get; set; } = 465;

    /// <summary>
    /// The SMTP username. For Hostinger this is the full mailbox address - in practice the same
    /// value as <see cref="SenderAddress"/>, because that is the only thing it will send as. Not
    /// every relay works that way, which is why the sender is a separate setting.
    /// </summary>
    public string SmtpUsername { get; set; } = string.Empty;

    /// <summary>
    /// The mailbox password, set in hPanel under Emails. It goes in .env, which is git-ignored, or
    /// in the deployment's own secret store - never in a committed file.
    /// </summary>
    public string SmtpPassword { get; set; } = string.Empty;

    // ---- Shared ------------------------------------------------------------------------------

    /// <summary>
    /// Who the message comes from. HOSTINGER SENDS ONLY AS THE AUTHENTICATED MAILBOX or one of the
    /// aliases configured for it in hPanel, and rejects anything else at send time rather than
    /// silently rewriting it the way Gmail did. Leaving it blank therefore resolves to
    /// <see cref="SmtpUsername"/>, which is the one address that is always accepted.
    /// </summary>
    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "YDot";

    /// <summary>
    /// The address a message is actually sent from.
    ///
    /// THE FALLBACK TO <see cref="SmtpUsername"/> IS GUARDED, and the guard stays even though
    /// Hostinger does not need it. Falling back works here, because the username is the mailbox
    /// address. It did not work under Resend, which authenticates as the literal string "resend":
    /// the unguarded fallback put "resend" in a From header, which MimeKit turns into a malformed
    /// address and the relay refuses, with a parse error that says nothing about the sender being
    /// unset. Falling back only when the username actually looks like an address costs nothing on
    /// a relay of this kind and turns the other kind into an empty sender, which
    /// <see cref="IsSenderConfigured"/> reports plainly.
    /// </summary>
    public string ResolvedSenderAddress =>
        !string.IsNullOrWhiteSpace(SenderAddress) ? SenderAddress.Trim()
        : SmtpUsername.Contains('@', StringComparison.Ordinal) ? SmtpUsername.Trim()
        : string.Empty;

    /// <summary>
    /// False when there is no usable From address, so the caller can say so once instead of
    /// letting every message fail in the transport with a parse error.
    /// </summary>
    public bool IsSenderConfigured => !string.IsNullOrWhiteSpace(ResolvedSenderAddress);

    /// <summary>
    /// STARTTLS: connect in the clear on 587, then upgrade the same connection.
    ///
    /// IGNORED ON THE DEFAULT PORT 465, AND ON 2465, which are a different thing entirely. They
    /// are implicit TLS
    /// (SMTPS) - the handshake happens before a single SMTP verb is spoken - and a client that
    /// opens one in the clear and waits for a greeting simply hangs until it times out. The
    /// transport picks the mode from the port rather than trusting these two to be set
    /// consistently, because the failure when they disagree is a silent thirty-second stall with
    /// nothing in the log.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Implicit TLS from the first byte. Left null, the transport infers it: true on 465 and
    /// 2465, false everywhere else. Set it only to override that for a relay on a non-standard
    /// port.
    /// </summary>
    public bool? UseImplicitTls { get; set; }

    /// <summary>True when this connection should negotiate TLS before speaking SMTP.</summary>
    public bool ResolvedUseImplicitTls => UseImplicitTls ?? SmtpPort is 465 or 2465;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When set, every message goes here instead of the real recipient, with the intended
    /// address noted in the subject. Makes it impossible to mail a real donor from a test
    /// database by accident.
    /// </summary>
    public string? RedirectAllToAddress { get; set; }

    public int MaximumRetries { get; set; } = 3;
}
