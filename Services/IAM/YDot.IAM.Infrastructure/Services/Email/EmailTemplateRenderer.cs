using System.Net;
using System.Text;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Services.Email;

/// <summary>One rendered message, ready to send.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Builds the outbound messages.
///
/// EVERY VALUE THAT COMES FROM DATA IS HTML-ENCODED. An Organisation name, a person name and
/// a rejection reason are all user-supplied, and dropping them raw into an HTML body is a
/// stored-XSS vector against whoever opens the mail in a web client. <see cref="Encode"/> is
/// applied to every substitution rather than the ones that look risky.
///
/// EVERY MESSAGE HAS A PLAIN-TEXT ALTERNATIVE. Not decoration: many corporate mail clients
/// strip HTML entirely, and an invitation that arrives as a blank message is an invitation
/// that never arrives.
///
/// THE BRANDING IS THE ORGANISATION'S, not the platform's. Somebody invited to Hope Foundation
/// should receive a message that says Hope Foundation, because one that says NGoPlanet reads
/// like phishing and gets deleted.
/// </summary>
public sealed class EmailTemplateRenderer
{
    public EmailMessage Invitation(
        User user, Tenant? tenant, BusinessUnit businessUnit, string activationUrl,
        DateTimeOffset expiresAtUtc, string? personalMessage)
    {
        var organisation = tenant?.Name ?? businessUnit.Name;
        var days = Math.Max(1, (int)Math.Ceiling((expiresAtUtc - DateTimeOffset.UtcNow).TotalDays));

        var intro = tenant is null
            ? $"You have been invited to the {Encode(businessUnit.Name)} platform."
            : $"You have been invited to join <strong>{Encode(tenant.Name)}</strong> on {Encode(businessUnit.Name)}.";

        var body = new StringBuilder()
            .Append($"<p>Hello {Encode(user.FirstName)},</p>")
            .Append($"<p>{intro}</p>");

        if (!string.IsNullOrWhiteSpace(personalMessage))
        {
            body.Append($"<blockquote style=\"border-left:3px solid #ddd;margin:16px 0;padding:8px 16px;color:#555;\">"
                        + $"{Encode(personalMessage)}</blockquote>");
        }

        body.Append("<p>Use the button below to choose a password and activate your account.</p>")
            .Append(Button(activationUrl, "Activate my account"))
            .Append($"<p style=\"color:#666;font-size:13px;\">This link expires in {days} day(s). "
                    + "If you were not expecting this invitation you can ignore this message.</p>");

        var text = new StringBuilder()
            .AppendLine($"Hello {user.FirstName},")
            .AppendLine()
            .AppendLine(tenant is null
                ? $"You have been invited to the {businessUnit.Name} platform."
                : $"You have been invited to join {tenant.Name} on {businessUnit.Name}.")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(personalMessage))
        {
            text.AppendLine(personalMessage).AppendLine();
        }

        text.AppendLine("Activate your account here:")
            .AppendLine(activationUrl)
            .AppendLine()
            .AppendLine($"This link expires in {days} day(s).");

        return Build(
            user.Email!,
            $"Activate your {organisation} account",
            body.ToString(), text.ToString(), tenant, businessUnit);
    }

    public EmailMessage PasswordReset(
        User user, Tenant? tenant, BusinessUnit businessUnit, string resetUrl, DateTimeOffset expiresAtUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((expiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes));

        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + "<p>We received a request to reset your password. Use the button below to choose a new one.</p>"
                   + Button(resetUrl, "Reset my password")
                   + $"<p style=\"color:#666;font-size:13px;\">This link expires in {minutes} minute(s) and can be "
                   + "used once. <strong>If you did not ask for this, you can ignore this message</strong> - your "
                   + "password has not changed.</p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + "We received a request to reset your password.\n\n"
                   + $"Reset it here:\n{resetUrl}\n\n"
                   + $"This link expires in {minutes} minute(s) and can be used once.\n"
                   + "If you did not ask for this, you can ignore this message - your password has not changed.";

        return Build(user.Email!, "Reset your password", body, text, tenant, businessUnit);
    }

    /// <summary>
    /// Confirmation that a password changed.
    ///
    /// Sent even though the person just did it, because the case that matters is the one where
    /// they did NOT. This is often the first warning somebody gets that their account was
    /// taken over.
    /// </summary>
    public EmailMessage PasswordChanged(
        User user, Tenant? tenant, BusinessUnit businessUnit, string? ipAddress)
    {
        var whenText = DateTimeOffset.UtcNow.ToString("dddd d MMMM yyyy 'at' HH:mm 'UTC'",
            System.Globalization.CultureInfo.InvariantCulture);

        var from = string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $" from {Encode(ipAddress)}";

        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + $"<p>Your password was changed on {Encode(whenText)}{from}.</p>"
                   + "<p><strong>If this was not you, reset your password immediately and tell your "
                   + "administrator.</strong></p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"Your password was changed on {whenText}"
                   + (string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $" from {ipAddress}") + ".\n\n"
                   + "If this was not you, reset your password immediately and tell your administrator.";

        return Build(user.Email!, "Your password was changed", body, text, tenant, businessUnit);
    }

    public EmailMessage MfaCode(
        User user, Tenant? tenant, BusinessUnit businessUnit, string code, DateTimeOffset expiresAtUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((expiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes));

        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + "<p>Your verification code is:</p>"
                   + $"<p style=\"font-size:32px;font-weight:700;letter-spacing:6px;margin:24px 0;"
                   + $"font-family:monospace;\">{Encode(code)}</p>"
                   + $"<p style=\"color:#666;font-size:13px;\">It expires in {minutes} minute(s). "
                   + "<strong>Nobody from support will ever ask you for this code.</strong></p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"Your verification code is: {code}\n\n"
                   + $"It expires in {minutes} minute(s).\n"
                   + "Nobody from support will ever ask you for this code.";

        return Build(user.Email!, $"{code} is your verification code", body, text, tenant, businessUnit);
    }

    public EmailMessage EmailConfirmation(
        User user, Tenant? tenant, BusinessUnit businessUnit, string confirmUrl, string targetEmail)
    {
        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + $"<p>Confirm that <strong>{Encode(targetEmail)}</strong> belongs to you.</p>"
                   + Button(confirmUrl, "Confirm this address")
                   + "<p style=\"color:#666;font-size:13px;\">If you did not ask for this, ignore this message.</p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"Confirm that {targetEmail} belongs to you:\n{confirmUrl}\n\n"
                   + "If you did not ask for this, ignore this message.";

        return Build(targetEmail, "Confirm your e-mail address", body, text, tenant, businessUnit);
    }

    /// <summary>
    /// Warns the OLD address that somebody is trying to change the sign-in identifier.
    ///
    /// The whole point of the message: it goes to the address being replaced, so the real
    /// owner finds out before the change lands rather than after.
    /// </summary>
    public EmailMessage LoginIdentifierChangeNotice(
        User user, Tenant? tenant, BusinessUnit businessUnit, string previousValue, string requestedValue)
    {
        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + $"<p>A request was made to change the sign-in details on your account from "
                   + $"<strong>{Encode(previousValue)}</strong> to <strong>{Encode(requestedValue)}</strong>.</p>"
                   + "<p><strong>If this was not you, contact your administrator immediately.</strong></p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"A request was made to change the sign-in details on your account from "
                   + $"{previousValue} to {requestedValue}.\n\n"
                   + "If this was not you, contact your administrator immediately.";

        return Build(previousValue, "Your sign-in details are being changed", body, text, tenant, businessUnit);
    }

    public EmailMessage AccountLocked(
        User user, Tenant? tenant, BusinessUnit businessUnit, DateTimeOffset lockoutEndUtc, string? ipAddress)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEndUtc - DateTimeOffset.UtcNow).TotalMinutes));
        var from = string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $" from {Encode(ipAddress)}";

        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + $"<p>Your account has been locked for {minutes} minute(s) after too many failed "
                   + $"sign-in attempts{from}.</p>"
                   + "<p>You can wait for the lock to lift, or reset your password - which also clears it.</p>"
                   + "<p><strong>If this was not you, reset your password and tell your administrator.</strong></p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"Your account has been locked for {minutes} minute(s) after too many failed sign-in attempts"
                   + (string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $" from {ipAddress}") + ".\n\n"
                   + "You can wait for the lock to lift, or reset your password - which also clears it.\n\n"
                   + "If this was not you, reset your password and tell your administrator.";

        return Build(user.Email!, "Your account is temporarily locked", body, text, tenant, businessUnit);
    }

    public EmailMessage Welcome(User user, Tenant? tenant, BusinessUnit businessUnit, string signInUrl)
    {
        var organisation = tenant?.Name ?? businessUnit.Name;

        var body = $"<p>Hello {Encode(user.FirstName)},</p>"
                   + $"<p>Your account at <strong>{Encode(organisation)}</strong> is now active.</p>"
                   + Button(signInUrl, "Sign in")
                   + $"<p style=\"color:#666;font-size:13px;\">Bookmark this address - it is where you sign in "
                   + "from now on.</p>";

        var text = $"Hello {user.FirstName},\n\n"
                   + $"Your account at {organisation} is now active.\n\n"
                   + $"Sign in here:\n{signInUrl}";

        return Build(user.Email!, $"Welcome to {organisation}", body, text, tenant, businessUnit);
    }

    public EmailMessage OrganisationSubmitted(Tenant tenant, BusinessUnit businessUnit, User recipient)
    {
        var body = $"<p>Hello {Encode(recipient.FirstName)},</p>"
                   + $"<p>We have received the details for <strong>{Encode(tenant.Name)}</strong> and they are "
                   + "now waiting for review.</p>"
                   + "<p>You will be told by e-mail once a decision has been made. No action is needed from you "
                   + "in the meantime.</p>";

        var text = $"Hello {recipient.FirstName},\n\n"
                   + $"We have received the details for {tenant.Name} and they are now waiting for review.\n\n"
                   + "You will be told by e-mail once a decision has been made.";

        return Build(recipient.Email!, $"{tenant.Name} submitted for approval", body, text, tenant, businessUnit);
    }

    public EmailMessage OrganisationApproved(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string organisationUrl)
    {
        var body = $"<p>Hello {Encode(recipient.FirstName)},</p>"
                   + $"<p><strong>{Encode(tenant.Name)}</strong> has been approved and is now active.</p>"
                   + "<p>You can sign in and start adding your team.</p>"
                   + Button(organisationUrl, $"Go to {Encode(tenant.Name)}");

        var text = $"Hello {recipient.FirstName},\n\n"
                   + $"{tenant.Name} has been approved and is now active.\n\n"
                   + $"Sign in here:\n{organisationUrl}";

        return Build(recipient.Email!, $"{tenant.Name} has been approved", body, text, tenant, businessUnit);
    }

    /// <summary>
    /// The rejection.
    ///
    /// The reason is the whole message. A refusal the recipient cannot act on is a dead end,
    /// which is why the reason is mandatory upstream and rendered prominently here.
    /// </summary>
    public EmailMessage OrganisationRejected(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason, string resubmitUrl)
    {
        var body = $"<p>Hello {Encode(recipient.FirstName)},</p>"
                   + $"<p>The details submitted for <strong>{Encode(tenant.Name)}</strong> could not be approved "
                   + "yet.</p>"
                   + "<p><strong>What needs to change:</strong></p>"
                   + $"<blockquote style=\"border-left:3px solid #d9534f;margin:16px 0;padding:8px 16px;"
                   + $"color:#555;\">{Encode(reason)}</blockquote>"
                   + "<p>Update the details and submit them again.</p>"
                   + Button(resubmitUrl, "Update our details");

        var text = $"Hello {recipient.FirstName},\n\n"
                   + $"The details submitted for {tenant.Name} could not be approved yet.\n\n"
                   + $"What needs to change:\n{reason}\n\n"
                   + $"Update the details and submit them again:\n{resubmitUrl}";

        return Build(recipient.Email!, $"{tenant.Name} needs more information", body, text, tenant, businessUnit);
    }

    public EmailMessage OrganisationSuspended(
        Tenant tenant, BusinessUnit businessUnit, User recipient, string reason)
    {
        var body = $"<p>Hello {Encode(recipient.FirstName)},</p>"
                   + $"<p><strong>{Encode(tenant.Name)}</strong> has been suspended. Users cannot sign in while "
                   + "this is in place.</p>"
                   + $"<blockquote style=\"border-left:3px solid #d9534f;margin:16px 0;padding:8px 16px;"
                   + $"color:#555;\">{Encode(reason)}</blockquote>"
                   + "<p>Contact support to discuss this.</p>";

        var text = $"Hello {recipient.FirstName},\n\n"
                   + $"{tenant.Name} has been suspended. Users cannot sign in while this is in place.\n\n"
                   + $"Reason:\n{reason}\n\nContact support to discuss this.";

        return Build(recipient.Email!, $"{tenant.Name} has been suspended", body, text, tenant, businessUnit);
    }

    public EmailMessage OrganisationAwaitingReview(
        Tenant tenant, BusinessUnit businessUnit, string reviewerEmail, string reviewUrl)
    {
        var body = "<p>Hello,</p>"
                   + $"<p><strong>{Encode(tenant.Name)}</strong> ({Encode(tenant.Code)}) has submitted its details "
                   + "and is waiting for review.</p>"
                   + Button(reviewUrl, "Review this organisation");

        var text = $"{tenant.Name} ({tenant.Code}) has submitted its details and is waiting for review.\n\n"
                   + $"Review it here:\n{reviewUrl}";

        return Build(reviewerEmail, $"Review required: {tenant.Name}", body, text, null, businessUnit);
    }

    public EmailMessage AccessRequestAwaitingDecision(
        User approver, User subject, Tenant? tenant, BusinessUnit businessUnit,
        string requestNumber, string reviewUrl)
    {
        var body = $"<p>Hello {Encode(approver.FirstName)},</p>"
                   + $"<p>Access request <strong>{Encode(requestNumber)}</strong> for "
                   + $"<strong>{Encode(subject.DisplayName)}</strong> is waiting for your decision.</p>"
                   + Button(reviewUrl, "Review this request");

        var text = $"Hello {approver.FirstName},\n\n"
                   + $"Access request {requestNumber} for {subject.DisplayName} is waiting for your decision.\n\n"
                   + $"Review it here:\n{reviewUrl}";

        return Build(approver.Email!, $"Approval needed: {requestNumber}", body, text, tenant, businessUnit);
    }

    public EmailMessage AccessRequestDecided(
        User requester, Tenant? tenant, BusinessUnit businessUnit,
        string requestNumber, bool approved, string? reason)
    {
        var outcome = approved ? "approved" : "rejected";

        var body = new StringBuilder()
            .Append($"<p>Hello {Encode(requester.FirstName)},</p>")
            .Append($"<p>Access request <strong>{Encode(requestNumber)}</strong> has been "
                    + $"<strong>{outcome}</strong>.</p>");

        if (!string.IsNullOrWhiteSpace(reason))
        {
            body.Append($"<blockquote style=\"border-left:3px solid #ddd;margin:16px 0;padding:8px 16px;"
                        + $"color:#555;\">{Encode(reason)}</blockquote>");
        }

        var text = $"Hello {requester.FirstName},\n\n"
                   + $"Access request {requestNumber} has been {outcome}."
                   + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"\n\n{reason}");

        return Build(
            requester.Email!, $"Access request {requestNumber} was {outcome}",
            body.ToString(), text, tenant, businessUnit);
    }

    public EmailMessage AccessReviewReminder(
        User reviewer, Tenant? tenant, BusinessUnit businessUnit,
        string reviewNumber, DateTimeOffset dueAtUtc, string reviewUrl)
    {
        var due = dueAtUtc.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var body = $"<p>Hello {Encode(reviewer.FirstName)},</p>"
                   + $"<p>Access review <strong>{Encode(reviewNumber)}</strong> is due on "
                   + $"<strong>{Encode(due)}</strong>.</p>"
                   + Button(reviewUrl, "Complete this review");

        var text = $"Hello {reviewer.FirstName},\n\n"
                   + $"Access review {reviewNumber} is due on {due}.\n\n"
                   + $"Complete it here:\n{reviewUrl}";

        return Build(reviewer.Email!, $"Access review {reviewNumber} is due", body, text, tenant, businessUnit);
    }

    /// <summary>Wraps a body in the shared shell, branded for the Organisation.</summary>
    private static EmailMessage Build(
        string to, string subject, string bodyHtml, string bodyText, Tenant? tenant, BusinessUnit businessUnit)
    {
        var brand = tenant?.Name ?? businessUnit.Name;
        var support = businessUnit.SupportEmail ?? businessUnit.ContactEmail;

        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>")
            .Append("<body style=\"margin:0;padding:0;background:#f5f6f8;"
                    + "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">"
                    + "<tr><td align=\"center\" style=\"padding:24px 12px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:560px;background:#ffffff;"
                    + "border-radius:8px;border:1px solid #e6e8eb;\" cellpadding=\"0\" cellspacing=\"0\">")
            .Append($"<tr><td style=\"padding:24px 28px 8px;border-bottom:1px solid #f0f1f3;\">"
                    + $"<span style=\"font-size:18px;font-weight:700;color:#1a1d21;\">{Encode(brand)}</span></td></tr>")
            .Append($"<tr><td style=\"padding:20px 28px;color:#2c3138;font-size:15px;line-height:1.6;\">"
                    + $"{bodyHtml}</td></tr>")
            .Append("<tr><td style=\"padding:16px 28px 24px;border-top:1px solid #f0f1f3;color:#8a9099;"
                    + "font-size:12px;line-height:1.5;\">")
            .Append($"Sent by {Encode(brand)} via {Encode(businessUnit.Name)}.")
            .Append(string.IsNullOrWhiteSpace(support)
                ? string.Empty
                : $" Need help? Contact <a href=\"mailto:{Encode(support)}\" "
                  + $"style=\"color:#5b6472;\">{Encode(support)}</a>.")
            .Append("<br>This is an automated message - please do not reply.")
            .Append("</td></tr></table></td></tr></table></body></html>");

        var text = new StringBuilder()
            .AppendLine(brand)
            .AppendLine(new string('-', Math.Min(brand.Length, 60)))
            .AppendLine()
            .AppendLine(bodyText)
            .AppendLine()
            .AppendLine($"Sent by {brand} via {businessUnit.Name}.")
            .AppendLine("This is an automated message - please do not reply.");

        return new EmailMessage(to, subject, html.ToString(), text.ToString());
    }

    private static string Button(string url, string label) =>
        $"<p style=\"margin:24px 0;\"><a href=\"{Encode(url)}\" "
        + "style=\"display:inline-block;padding:12px 24px;background:#2563eb;color:#ffffff;"
        + "text-decoration:none;border-radius:6px;font-weight:600;font-size:15px;\">"
        + $"{label}</a></p>"
        + $"<p style=\"color:#8a9099;font-size:12px;word-break:break-all;\">"
        + $"Or paste this into your browser:<br>{Encode(url)}</p>";

    /// <summary>
    /// HTML-encodes a value. Applied to EVERY substitution, including ones that look safe -
    /// an Organisation name is user-supplied and a reason is free text.
    /// </summary>
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
