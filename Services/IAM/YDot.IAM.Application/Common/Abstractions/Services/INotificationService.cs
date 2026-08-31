using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// Outbound e-mail.
///
/// EVERY MESSAGE IS ORGANISATION-BRANDED. The Tenant is passed in rather than read from
/// ambient context, because several of these are sent from a background or platform path
/// where there is no ambient Organisation — and an invitation that arrives branded as the
/// wrong Organisation is worse than one that never arrives.
///
/// THE LINKS ARE BUILT FROM THE ORGANISATION OWN HOST. An invitation for ten1 points at
/// ten1.ngoplanet.com, not at the platform host, so the person lands on the right sign-in
/// page and the right Tenant is resolved when they get there.
///
/// Nothing here throws on a delivery failure. A user is still created when the mail relay is
/// down; the failure is logged and the invitation can be re-sent. Rolling back an account
/// creation because SMTP hiccuped would be a far worse outcome.
/// </summary>
public interface INotificationService
{
    /// <summary>Invitation with the activation link. Used for TenantAdmin and ordinary users alike.</summary>
    Task SendInvitationAsync(
        User user, UserInvitation invitation, Tenant? tenant, BusinessUnit businessUnit,
        string activationUrl, CancellationToken cancellationToken);

    /// <summary>Password reset link.</summary>
    Task SendPasswordResetAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string resetUrl,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>Confirmation that a password changed. A warning shot if it was not them.</summary>
    Task SendPasswordChangedAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>One-time code for sign-in, enrolment or step-up.</summary>
    Task SendMfaCodeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string code,
        DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>Confirmation link for a new e-mail address.</summary>
    Task SendEmailConfirmationAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string confirmUrl,
        string targetEmail, CancellationToken cancellationToken);

    /// <summary>Tells the OLD address that a login-identifier change was requested.</summary>
    Task SendLoginIdentifierChangeNoticeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string previousValue,
        string requestedValue, CancellationToken cancellationToken);

    /// <summary>Tells somebody their account was locked, and when it frees up.</summary>
    Task SendAccountLockedAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, DateTimeOffset lockoutEndUtc,
        string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Welcome after activation, with a link to sign in.</summary>
    Task SendWelcomeAsync(
        User user, Tenant? tenant, BusinessUnit businessUnit, string signInUrl,
        CancellationToken cancellationToken);

    // ---- Organisation onboarding ---------------------------------------------------------

    /// <summary>Tells the TenantAdmin their Organisation profile was received.</summary>
    Task SendOrganisationSubmittedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, CancellationToken cancellationToken);

    /// <summary>Tells the TenantAdmin their Organisation is live, with the link to it.</summary>
    Task SendOrganisationApprovedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string organisationUrl,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tells the TenantAdmin the Organisation was turned down, and why. The reason is
    /// mandatory upstream precisely so this message can be actionable.
    /// </summary>
    Task SendOrganisationRejectedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason,
        string resubmitUrl, CancellationToken cancellationToken);

    /// <summary>Tells the TenantAdmin the Organisation was suspended.</summary>
    Task SendOrganisationSuspendedAsync(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason,
        CancellationToken cancellationToken);

    /// <summary>Tells SuperAdmin an Organisation is waiting for review.</summary>
    Task SendOrganisationAwaitingReviewAsync(
        Tenant tenant, BusinessUnit businessUnit, IReadOnlyList<string> reviewerEmails,
        string reviewUrl, CancellationToken cancellationToken);

    /// <summary>Notifies a reviewer that an access request needs a decision.</summary>
    Task SendAccessRequestAwaitingDecisionAsync(
        User approver, User subject, Tenant? tenant, BusinessUnit businessUnit,
        string requestNumber, string reviewUrl, CancellationToken cancellationToken);

    /// <summary>Tells the requester what was decided.</summary>
    Task SendAccessRequestDecidedAsync(
        User requester, Tenant? tenant, BusinessUnit businessUnit, string requestNumber,
        bool approved, string? reason, CancellationToken cancellationToken);

    /// <summary>Reminds a reviewer that an access review is due.</summary>
    Task SendAccessReviewReminderAsync(
        User reviewer, Tenant? tenant, BusinessUnit businessUnit, string reviewNumber,
        DateTimeOffset dueAtUtc, string reviewUrl, CancellationToken cancellationToken);
}
