namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// Outbound mail for receipt delivery, bound from the EmailSettings section.
///
/// THE SHAPE MATCHES IAM'S DELIBERATELY, so one set of environment variables configures mail for
/// the whole platform rather than each service inventing its own key names - which is how one
/// service ends up sending and another silently not.
///
/// THE CREDENTIALS LIVE ON THE SERVER. They are never in the Angular bundle and never reach a
/// browser; the UI asks the API to send a receipt and the API does the rest.
///
/// ONE TRANSPORT: AN SMTP RELAY, and by default Hostinger's, on port 465 with implicit TLS. A
/// Resend API key, an Elastic Email credential and a Gmail App Password each held this slot
/// before it; all are gone, along with the provider switch that chose between them.
///
/// THIS SECTION NOW CARRIES THE IMPLICIT-TLS SETTINGS IAM'S ALWAYS HAD. It did not while
/// SmtpEmailSender was built on System.Net.Mail, which has no implicit-TLS mode - so the platform
/// moving to a 465 relay stalled every receipt for twenty seconds and filed it as undelivered
/// while IAM sent normally on the same credentials. That sender is MailKit now and these two
/// properties are what tell it which handshake to speak.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "EmailSettings";

    /// <summary>
    /// When false nothing is sent and every message is written to the log instead.
    ///
    /// That is enough to walk the whole donate - capture - receipt - deliver flow with no mail
    /// credentials at all, which is how the development data is meant to be exercised.
    /// </summary>
    public bool Enabled { get; set; }

    // ---- SMTP --------------------------------------------------------------------------------

    /// <summary>
    /// Empty by default, and SmtpEmailSender treats empty as "off" regardless of
    /// <see cref="Enabled"/>. Two independent ways to be switched off is the intended state for a
    /// service that mails tax documents: it has to be turned on deliberately.
    /// </summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>
    /// 465, Hostinger's implicit-TLS port. 587 is the same relay over STARTTLS and also works;
    /// <see cref="ResolvedUseImplicitTls"/> picks the handshake from this number, so either is a
    /// one-line change. 25 is closed by Hostinger and by most networks.
    ///
    /// 465 IS SAFE FOR THIS SERVICE NOW, and was not before. See the note on the class.
    /// </summary>
    public int SmtpPort { get; set; } = 465;

    /// <summary>
    /// The SMTP username. For Hostinger this is the full mailbox address, which is also the
    /// address it will send as - see <see cref="SenderAddress"/>.
    /// </summary>
    public string SmtpUsername { get; set; } = string.Empty;

    /// <summary>
    /// The mailbox password, set in hPanel under Emails. It goes in .env, which is git-ignored, or
    /// in the deployment's own secret store - never in a committed file.
    /// </summary>
    public string SmtpPassword { get; set; } = string.Empty;

    // ---- Shared ------------------------------------------------------------------------------

    /// <summary>
    /// Who the receipt comes from. HOSTINGER SENDS ONLY AS THE AUTHENTICATED MAILBOX or one of its
    /// aliases, and rejects anything else at send time - so in practice this equals
    /// <see cref="SmtpUsername"/>, and leaving it blank resolves to exactly that.
    /// </summary>
    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "YDot Donations";

    /// <summary>
    /// The address a receipt is actually sent from.
    ///
    /// THE FALLBACK TO <see cref="SmtpUsername"/> IS GUARDED, and the guard is why this is not a
    /// one-line expression. It works for a relay like Hostinger, whose username IS the mailbox
    /// address. It does not work for one that authenticates with a name - Resend, which held this
    /// slot before, uses the literal string "resend" - and the unguarded version handed that
    /// straight to the From header, where it became a parse failure filed against a real receipt:
    /// sitting in the undelivered queue, blamed on an invalid address that was actually a missing
    /// setting. Kept because the next provider may well be the other kind again.
    /// </summary>
    public string ResolvedSenderAddress =>
        !string.IsNullOrWhiteSpace(SenderAddress) ? SenderAddress.Trim()
        : SmtpUsername.Contains('@', StringComparison.Ordinal) ? SmtpUsername.Trim()
        : string.Empty;

    /// <summary>
    /// False when there is no usable From address, so the sender can say so once instead of
    /// filing a FormatException against every receipt it tries to deliver.
    /// </summary>
    public bool IsSenderConfigured => !string.IsNullOrWhiteSpace(ResolvedSenderAddress);

    /// <summary>
    /// STARTTLS: connect in the clear on 587, then upgrade the same connection.
    ///
    /// IGNORED ON 465 AND 2465, which are implicit TLS - a different handshake, chosen from the
    /// port by <see cref="ResolvedUseImplicitTls"/> rather than from this flag, because the
    /// failure when a port and a flag disagree is a silent stall rather than an error.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Implicit TLS from the first byte. Left null, the transport infers it: true on 465 and 2465,
    /// false everywhere else. Set it only to override that for a relay on a non-standard port.
    /// </summary>
    public bool? UseImplicitTls { get; set; }

    /// <summary>True when this connection should negotiate TLS before speaking SMTP.</summary>
    public bool ResolvedUseImplicitTls => UseImplicitTls ?? SmtpPort is 465 or 2465;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When set, every message goes here instead of the real recipient, with the intended address
    /// noted in the subject.
    ///
    /// IT MATTERS MORE IN THIS SERVICE THAN IN IAM. A test database restored from production
    /// contains real donors and real receipts, and mailing them tax documents from a test
    /// environment is a mistake that only has to happen once.
    /// </summary>
    public string? RedirectAllToAddress { get; set; }
}
