using FluentValidation;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways.Validators;

/// <summary>
/// Shape checks on the save request.
///
/// WHAT IS HERE AND WHAT IS DELIBERATELY NOT. These rules are about the SHAPE of a request - a
/// URL that is a URL, a currency that is three letters, a list drawn from the catalogue. The
/// rules about whether a configuration makes SENSE - an active row needing a key, one active row
/// per environment, the version having to match - live in the handler, because they need the
/// stored row to decide and a validator has none.
///
/// THE CREDENTIALS ARE BARELY VALIDATED, AND THAT IS INTENTIONAL. There is a maximum length and
/// nothing else. A rule asserting that a Razorpay key starts <c>rzp_</c> would be right today
/// and would lock an organisation out of its own account the week Razorpay issues a key in a new
/// format. The screen WARNS about a prefix that looks wrong - see the catalogue's
/// TestKeyPrefix - and the Test button settles it against the provider, which is the only
/// authority on whether a credential is valid.
/// </summary>
public sealed class UpsertPaymentGatewayConfigurationRequestValidator
    : AbstractValidator<UpsertPaymentGatewayConfigurationRequest>
{
    /// <summary>Long enough for any provider's credential, short enough to bound the column.</summary>
    private const int MaximumCredentialLength = 500;

    public UpsertPaymentGatewayConfigurationRequestValidator()
    {
        RuleFor(request => request.Provider)
            .NotEqual(PaymentGatewayProvider.None)
            .WithMessage("Choose a payment gateway.")
            .IsInEnum()
            .WithMessage("That payment gateway is not one this platform supports.");

        RuleFor(request => request.Environment)
            .IsInEnum()
            .WithMessage("Choose sandbox or production.");

        RuleFor(request => request.DisplayName)
            .MaximumLength(150);

        RuleFor(request => request.MerchantId)
            .MaximumLength(150);

        RuleFor(request => request.ApiKey)
            .MaximumLength(MaximumCredentialLength);

        RuleFor(request => request.SecretKey)
            .MaximumLength(MaximumCredentialLength);

        RuleFor(request => request.WebhookSecret)
            .MaximumLength(MaximumCredentialLength);

        // A WEBHOOK URL MUST BE ABSOLUTE AND MUST BE HTTPS IN PRODUCTION. A provider posting a
        // payment outcome over plain HTTP puts the donation record on the wire in clear, and a
        // relative URL is one the provider simply cannot call at all.
        RuleFor(request => request.WebhookUrl)
            .Must(BeAnAbsoluteUrl)
            .When(request => !string.IsNullOrWhiteSpace(request.WebhookUrl))
            .WithMessage("Enter the full webhook address, starting with https://")
            .MaximumLength(500);

        RuleFor(request => request.WebhookUrl)
            .Must(url => url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .When(request => request.Environment == PaymentGatewayEnvironment.Production
                             && !string.IsNullOrWhiteSpace(request.WebhookUrl))
            .WithMessage(
                "A production webhook has to be https. Over plain http the payment outcome "
                + "travels in clear and can be read or altered in transit.");

        RuleFor(request => request.ReturnUrl)
            .Must(BeAnAbsoluteUrl)
            .When(request => !string.IsNullOrWhiteSpace(request.ReturnUrl))
            .WithMessage("Enter the full return address, starting with https://")
            .MaximumLength(500);

        RuleFor(request => request.SettlementCurrencyCode)
            .NotEmpty()
            .WithMessage("Name the currency this merchant account settles in.")
            .Length(3)
            .WithMessage("A currency code is exactly three letters, such as INR.")
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("A currency code is three letters, such as INR.");

        // FIFTEEN MINUTES IS RAZORPAY'S OWN FLOOR - it refuses a shorter expiry outright - and a
        // day at the top end because a link that outlives the campaign it belongs to is a link
        // somebody pays against after the appeal has closed.
        RuleFor(request => request.PaymentLinkValidityMinutes)
            .InclusiveBetween(15, 1440)
            .WithMessage("A payment link stays valid for between 15 minutes and 24 hours.");

        RuleForEach(request => request.SubscribedEvents)
            .Must(code => PaymentGatewayCatalogue.WebhookEventCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            .When(request => request.SubscribedEvents is not null)
            .WithMessage("That is not an event this platform can handle.");

        RuleForEach(request => request.EnabledMethods)
            .Must(code => PaymentGatewayCatalogue.PaymentMethodCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            .When(request => request.EnabledMethods is not null)
            .WithMessage("That is not a payment method this platform offers.");

        RuleFor(request => request.Notes)
            .MaximumLength(2000);

        RuleFor(request => request.Reason)
            .MaximumLength(1000);
    }

    private static bool BeAnAbsoluteUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

/// <summary>Shape checks on activate and deactivate.</summary>
public sealed class ChangePaymentGatewayStatusRequestValidator
    : AbstractValidator<ChangePaymentGatewayStatusRequest>
{
    public ChangePaymentGatewayStatusRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Reload the screen and try again.");

        RuleFor(request => request.Reason)
            .MaximumLength(1000);
    }
}

/// <summary>Shape checks on delete. The reason is required, and the handler says why.</summary>
public sealed class DeletePaymentGatewayConfigurationRequestValidator
    : AbstractValidator<DeletePaymentGatewayConfigurationRequest>
{
    public DeletePaymentGatewayConfigurationRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Reload the screen and try again.");

        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithMessage("Say why this gateway configuration is being removed.")
            .MaximumLength(1000);
    }
}
