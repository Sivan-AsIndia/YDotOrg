using FluentValidation;
using YDot.PAY.Application.Features.Gateway.DTOs;

namespace YDot.PAY.Application.Features.Gateway.Validators;

/// <summary>
/// Validator for the gateway account.
///
/// THE URL CHECKS MATTER MORE HERE THAN THEY LOOK. The return URL is where a donor is sent after
/// paying and the webhook URL is where the provider posts payment outcomes - a malformed one
/// means a donor stranded on an error page, or captures that never reach us at all.
/// </summary>
public sealed class UpsertGatewayAccountRequestValidator : AbstractValidator<UpsertGatewayAccountRequest>
{
    public UpsertGatewayAccountRequestValidator()
    {
        RuleFor(request => request.GatewayName)
            .NotEmpty().WithMessage("Name the payment provider.")
            .MaximumLength(50);

        RuleFor(request => request.MerchantId)
            .NotEmpty().WithMessage("Enter the merchant id the provider assigned.")
            .MaximumLength(200);

        RuleFor(request => request.SettlementCurrencyCode)
            .NotEmpty().Length(3)
            .WithMessage("Use a three-letter currency code, such as INR.");

        RuleFor(request => request.ReturnUrl)
            .Must(BeAnAbsoluteHttpUrl)
            .WithMessage("Enter a full web address beginning http:// or https://.")
            .When(request => !string.IsNullOrWhiteSpace(request.ReturnUrl));

        RuleFor(request => request.WebhookUrl)
            .Must(BeAnAbsoluteHttpUrl)
            .WithMessage("Enter a full web address beginning http:// or https://.")
            .When(request => !string.IsNullOrWhiteSpace(request.WebhookUrl));

        // Five minutes is short enough to be safe and long enough for a donor to finish typing
        // their card details; a day is the longest a link should ever live.
        RuleFor(request => request.PaymentLinkValidityMinutes)
            .InclusiveBetween(5, 1440)
            .WithMessage("A payment link should last between 5 minutes and 24 hours.");

        RuleFor(request => request.EnabledMethods).MaximumLength(500);
        RuleFor(request => request.Notes).MaximumLength(2000);

        // A REFERENCE, NOT A SECRET. Anything long enough to be an actual key is refused, because
        // a merchant secret in a request body ends up in a request log and a proxy buffer.
        RuleFor(request => request.ApiKeyReference)
            .MaximumLength(200)
            .When(request => !string.IsNullOrWhiteSpace(request.ApiKeyReference));

        RuleFor(request => request.WebhookSecretReference)
            .MaximumLength(200)
            .When(request => !string.IsNullOrWhiteSpace(request.WebhookSecretReference));
    }

    /// <summary>
    /// Whether a string is an absolute http or https URL.
    ///
    /// The SCHEME check is the point rather than the parse: <c>Uri.TryCreate</c> alone accepts
    /// <c>javascript:</c> and <c>file:</c>, either of which stored as a return URL would be a
    /// link the application later hands to a browser.
    /// </summary>
    private static bool BeAnAbsoluteHttpUrl(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
