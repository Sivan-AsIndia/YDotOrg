using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Services.Email;

/// <summary>
/// Sends the outbound mail, whichever relay is configured.
///
/// THE PROVIDER IS A DETAIL, THE POLICY IS NOT. This class decides whether a message is sent
/// at all, who it really goes to, and how often a failure is retried; an <see cref="IEmailTransport"/>
/// decides only how one message reaches one relay. Moving from a Gmail App Password to Elastic
/// Email, then Resend, and now a Hostinger mailbox, therefore changed configuration and nothing
/// else here.
///
/// NOTHING HERE EVER THROWS. That is the single most important decision in this file. A user
/// is created, an Organisation is approved, a password is reset — and then we try to send a
/// message. If the relay is down, rolling back a committed Organisation because SMTP hiccuped
/// would be a far worse outcome than an e-mail that has to be re-sent. Every failure is logged
/// with enough detail to find and re-send it, and the caller carries on.
///
/// WITH <c>EmailSettings.Enabled</c> OFF, every message is written to the log instead of being
/// sent, including the activation and reset links. That is enough to walk the whole invite →
/// activate → sign-in flow with no credentials at all, which is how the development seed data
/// is meant to be exercised.
///
/// <c>RedirectAllToAddress</c> sends everything to one mailbox with the intended recipient in
/// the subject. It exists so that a test database restored from production cannot mail real
/// donors, which is a mistake that only has to happen once.
/// </summary>
public sealed class EmailNotificationService(
    EmailTemplateRenderer renderer,
    IEmailTransport transport,
    IOptions<EmailSettings> emailOptions,
    ILogger<EmailNotificationService> logger) : INotificationService
{
    private readonly EmailSettings _settings = emailOptions.Value;

    public Task SendInvitationAsync(
        User user, UserInvitation invitation, Tenant? tenant, BusinessUnit businessUnit,
        string activationUrl, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.Invitation(user, tenant, businessUnit, activationUrl,
                invitation.ExpiresAtUtc, invitation.Message),
            "invitation", activationUrl, cancellationToken);

    public Task SendPasswordResetAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string resetUrl,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.PasswordReset(user, tenant, businessUnit, resetUrl, expiresAtUtc),
            "password-reset", resetUrl, cancellationToken);

    public Task SendPasswordChangedAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string? ipAddress,
        CancellationToken cancellationToken) =>
        SendAsync(
            renderer.PasswordChanged(user, tenant, businessUnit, ipAddress),
            "password-changed", null, cancellationToken);

    public Task SendMfaCodeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string code,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.MfaCode(user, tenant, businessUnit, code, expiresAtUtc),
            // The code itself is logged when the relay is off, because otherwise the MFA flow
            // is impossible to walk in development.
            "mfa-code", code, cancellationToken);

    public Task SendEmailConfirmationAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string confirmUrl,
        string targetEmail, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.EmailConfirmation(user, tenant, businessUnit, confirmUrl, targetEmail),
            "email-confirmation", confirmUrl, cancellationToken);

    public Task SendLoginIdentifierChangeNoticeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string previousValue,
        string requestedValue, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.LoginIdentifierChangeNotice(user, tenant, businessUnit, previousValue, requestedValue),
            "identifier-change-notice", null, cancellationToken);

    public Task SendAccountLockedAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, DateTimeOffset lockoutEndUtc,
        string? ipAddress, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.AccountLocked(user, tenant, businessUnit, lockoutEndUtc, ipAddress),
            "account-locked", null, cancellationToken);

    public Task SendWelcomeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string signInUrl,
        CancellationToken cancellationToken) =>
        SendAsync(
            renderer.Welcome(user, tenant, businessUnit, signInUrl),
            "welcome", signInUrl, cancellationToken);

    public Task SendOrganisationSubmittedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.OrganisationSubmitted(tenant, businessUnit, recipient),
            "organisation-submitted", null, cancellationToken);

    public Task SendOrganisationApprovedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string organisationUrl,
        CancellationToken cancellationToken) =>
        SendAsync(
            renderer.OrganisationApproved(tenant, businessUnit, recipient, organisationUrl),
            "organisation-approved", organisationUrl, cancellationToken);

    public Task SendOrganisationRejectedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason,
        string resubmitUrl, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.OrganisationRejected(tenant, businessUnit, recipient, reason, resubmitUrl),
            "organisation-rejected", resubmitUrl, cancellationToken);

    public Task SendOrganisationSuspendedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason,
        CancellationToken cancellationToken) =>
        SendAsync(
            renderer.OrganisationSuspended(tenant, businessUnit, recipient, reason),
            "organisation-suspended", null, cancellationToken);

    /// <summary>
    /// Tells every reviewer. Sent one at a time rather than as a single message with several
    /// recipients, so no reviewer sees the others addresses.
    /// </summary>
    public async Task SendOrganisationAwaitingReviewAsync(
        Tenant tenant, BusinessUnit businessUnit, IReadOnlyList<string> reviewerEmails,
        string reviewUrl, CancellationToken cancellationToken)
    {
        foreach (var reviewer in reviewerEmails.Where(email => !string.IsNullOrWhiteSpace(email)))
        {
            await SendAsync(
                renderer.OrganisationAwaitingReview(tenant, businessUnit, reviewer, reviewUrl),
                "organisation-awaiting-review", reviewUrl, cancellationToken);
        }
    }

    public Task SendAccessRequestAwaitingDecisionAsync(
        User approver, User subject, Tenant? tenant, BusinessUnit businessUnit,
        string requestNumber, string reviewUrl, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.AccessRequestAwaitingDecision(
                approver, subject, tenant, businessUnit, requestNumber, reviewUrl),
            "access-request-awaiting", reviewUrl, cancellationToken);

    public Task SendAccessRequestDecidedAsync(
        User requester, Tenant? tenant, BusinessUnit businessUnit, string requestNumber,
        bool approved, string? reason, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.AccessRequestDecided(requester, tenant, businessUnit, requestNumber, approved, reason),
            "access-request-decided", null, cancellationToken);

    public Task SendAccessReviewReminderAsync(
        User reviewer, Tenant? tenant, BusinessUnit businessUnit, string reviewNumber,
        DateTimeOffset dueAtUtc, string reviewUrl, CancellationToken cancellationToken) =>
        SendAsync(
            renderer.AccessReviewReminder(reviewer, tenant, businessUnit, reviewNumber, dueAtUtc, reviewUrl),
            "access-review-reminder", reviewUrl, cancellationToken);

    /// <summary>
    /// The single send path.
    ///
    /// <paramref name="loggableLink"/> is the link or code that this message carries. It is
    /// written to the log ONLY when the relay is switched off, so a developer can complete the
    /// flow — never when mail is actually being sent, because a live activation link in a log
    /// file is a live activation link for anybody who can read logs.
    /// </summary>
    private async Task SendAsync(
        EmailMessage message, string kind, string? loggableLink, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation(
                "E-mail is disabled. Would have sent {Kind} to {Recipient} with subject {Subject}. Link: {Link}",
                kind, message.To, message.Subject, loggableLink ?? "(none)");

            return;
        }

        if (string.IsNullOrWhiteSpace(message.To))
        {
            logger.LogWarning("Skipped a {Kind} message because it had no recipient.", kind);
            return;
        }

        var recipient = string.IsNullOrWhiteSpace(_settings.RedirectAllToAddress)
            ? message.To
            : _settings.RedirectAllToAddress;

        var subject = string.IsNullOrWhiteSpace(_settings.RedirectAllToAddress)
            ? message.Subject
            : $"[to: {message.To}] {message.Subject}";

        // NO SENDER, NO SEND. EmailSettings.ResolvedSenderAddress falls back to the authenticated
        // SMTP account only when that is itself an address. On Hostinger it is, so a blank sender
        // resolves fine; on a relay that authenticates with a name rather than an address - Resend,
        // which held this slot before, uses the literal string "resend" - a deployment that forgot
        // EmailSettings__SenderAddress arrives here with nothing usable. Saying so once, here,
        // beats letting every message die three attempts deep in the transport with a header parse
        // error that names neither the setting nor the reason.
        var senderAddress = _settings.ResolvedSenderAddress;

        if (!_settings.IsSenderConfigured)
        {
            logger.LogError(
                "Cannot send {Kind} to {Recipient}: no sender address is configured. Set "
                + "EmailSettings:SenderAddress to an address on a domain verified with the relay "
                + "at {Relay} - it cannot be inferred from the SMTP username {Username}.",
                kind, recipient, _settings.SmtpHost, _settings.SmtpUsername);

            return;
        }

        for (var attempt = 1; attempt <= Math.Max(1, _settings.MaximumRetries); attempt++)
        {
            try
            {
                var providerReference = await transport.SendAsync(
                    senderAddress, _settings.SenderName, recipient, subject,
                    message.HtmlBody, message.TextBody, cancellationToken);

                logger.LogInformation(
                    "Sent {Kind} to {Recipient} with subject {Subject} via {Relay}. Reference: {Reference}",
                    kind, recipient, subject, _settings.SmtpHost,
                    string.IsNullOrWhiteSpace(providerReference) ? "(none)" : providerReference);

                return;
            }
            catch (EmailTransportException exception)
            {
                var isLastAttempt = attempt >= Math.Max(1, _settings.MaximumRetries);

                logger.Log(
                    isLastAttempt ? LogLevel.Error : LogLevel.Warning,
                    exception,
                    "Attempt {Attempt} to send {Kind} to {Recipient} failed.{Outcome}",
                    attempt, kind, recipient,
                    isLastAttempt
                        ? " Giving up - the message can be re-sent from the application."
                        : " Retrying.");

                if (isLastAttempt)
                {
                    // Swallowed on purpose. See the note at the top of this class: a delivery
                    // failure must never undo the operation that has already been committed.
                    return;
                }

                // A short linear back-off. A transient relay failure usually clears in seconds.
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }
}
