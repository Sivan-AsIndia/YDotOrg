using FluentValidation;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Organisations.Validators;

/// <summary>
/// Validators for the Organisation slice.
///
/// A NOTE ON WHAT IS NOT HERE. The profile validator checks LENGTHS and FORMATS only; it does
/// not require the fields. Completeness is enforced at SUBMISSION instead, by
/// <c>OrganisationMappingConfig.OutstandingProfileFields</c>, so a TenantAdmin can save a
/// half-finished profile and come back to it rather than losing work whenever they have to go
/// and find a certificate.
/// </summary>
public sealed class CreateOrganisationRequestValidator : AbstractValidator<CreateOrganisationRequest>
{
    public CreateOrganisationRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Enter the organisation name.")
            .MaximumLength(200);

        // The subdomain is what resolves an anonymous sign-in to an Organisation, so it is
        // validated against the value object rather than a loose pattern - which also applies
        // the reserved-word list.
        RuleFor(request => request.Subdomain)
            .NotEmpty().WithMessage("Choose a web address for the organisation.")
            .Must(value => SubdomainValue.TryParse(value) is not null)
            .WithMessage(request => SubdomainValue.IsReserved(request.Subdomain)
                ? "That address is reserved by the platform. Choose another."
                : "Use 1 to 63 lower-case letters, digits or hyphens, not starting or ending with a hyphen.");

        RuleFor(request => request.AdminEmail)
            .NotEmpty().WithMessage("Enter the administrator e-mail address.")
            .Must(value => EmailValue.TryParse(value) is not null)
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request.AdminFirstName)
            .NotEmpty().WithMessage("Enter the administrator first name.")
            .MaximumLength(80);

        RuleFor(request => request.AdminLastName)
            .NotEmpty().WithMessage("Enter the administrator last name.")
            .MaximumLength(80);

        RuleFor(request => request.AdminUsername)
            .Must(value => UsernameValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.AdminUsername))
            .WithMessage("Use 3 to 64 letters, digits, dots, hyphens or underscores.");

        RuleFor(request => request.Code)
            .MaximumLength(50)
            .When(request => !string.IsNullOrWhiteSpace(request.Code));

        RuleFor(request => request.LegalName).MaximumLength(250);
        RuleFor(request => request.OrganisationType).MaximumLength(100);

        RuleFor(request => request.MaximumUsers)
            .GreaterThan(0).WithMessage("The user limit must be at least 1.")
            .When(request => request.MaximumUsers.HasValue);

        RuleFor(request => request.DefaultCurrency)
            .Length(3).WithMessage("Use a three-letter currency code, such as INR.")
            .When(request => !string.IsNullOrWhiteSpace(request.DefaultCurrency));

        RuleFor(request => request.InvitationMessage).MaximumLength(1000);
    }
}

public sealed class UpdateOrganisationProfileRequestValidator
    : AbstractValidator<UpdateOrganisationProfileRequest>
{
    public UpdateOrganisationProfileRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("Reload the page and try again.");

        RuleFor(request => request.Name).MaximumLength(200);
        RuleFor(request => request.LegalName).MaximumLength(250);
        RuleFor(request => request.RegistrationNumber).MaximumLength(100);
        RuleFor(request => request.TaxIdentificationNumber).MaximumLength(100);
        RuleFor(request => request.PanNumber).MaximumLength(20);
        RuleFor(request => request.GstNumber).MaximumLength(30);
        RuleFor(request => request.OrganisationType).MaximumLength(100);
        RuleFor(request => request.Description).MaximumLength(2000);
        RuleFor(request => request.WebsiteUrl).MaximumLength(500);
        RuleFor(request => request.LogoUrl).MaximumLength(500);
        RuleFor(request => request.ContactPersonName).MaximumLength(200);
        RuleFor(request => request.AddressLine1).MaximumLength(250);
        RuleFor(request => request.AddressLine2).MaximumLength(250);
        RuleFor(request => request.City).MaximumLength(120);
        RuleFor(request => request.State).MaximumLength(120);
        RuleFor(request => request.Country).MaximumLength(120);
        RuleFor(request => request.PostalCode).MaximumLength(20);

        RuleFor(request => request.ContactEmail)
            .Must(value => EmailValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.ContactEmail))
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request)
            .Must(request => MobileNumberValue.TryParse(
                request.ContactPhoneCountryCode, request.ContactPhone) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.ContactPhone))
            .WithName(nameof(UpdateOrganisationProfileRequest.ContactPhone))
            .WithMessage("Enter a valid phone number with its country code.");

        // An establishment date in the future is always a typo.
        RuleFor(request => request.EstablishedOn)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow)
            .When(request => request.EstablishedOn.HasValue)
            .WithMessage("The establishment date cannot be in the future.");
    }
}

public sealed class ReviewOrganisationRequestValidator : AbstractValidator<ReviewOrganisationRequest>
{
    public ReviewOrganisationRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        // A refusal the TenantAdmin cannot act on is a dead end rather than a decision.
        RuleFor(request => request.Reason)
            .NotEmpty()
            .When(request => !request.Approved)
            .WithMessage("Give a reason so the organisation can correct and resubmit.")
            .MaximumLength(2000);

        RuleFor(request => request.Notes).MaximumLength(2000);
    }
}

public sealed class SuspendOrganisationRequestValidator : AbstractValidator<SuspendOrganisationRequest>
{
    public SuspendOrganisationRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Give a reason for the suspension.")
            .MaximumLength(2000);

        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class ArchiveOrganisationRequestValidator : AbstractValidator<ArchiveOrganisationRequest>
{
    public ArchiveOrganisationRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Give a reason for archiving this organisation.")
            .MaximumLength(2000);

        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class AddOrganisationDomainRequestValidator : AbstractValidator<AddOrganisationDomainRequest>
{
    public AddOrganisationDomainRequestValidator() =>
        RuleFor(request => request.HostName)
            .NotEmpty().WithMessage("Enter the web address.")
            .Must(value => HostNameValue.TryParse(value) is not null)
            .WithMessage("That web address is not valid.");
}

public sealed class ReviewOrganisationDocumentRequestValidator
    : AbstractValidator<ReviewOrganisationDocumentRequest>
{
    public ReviewOrganisationDocumentRequestValidator()
    {
        RuleFor(request => request.DocumentId).NotEmpty();

        RuleFor(request => request.Notes)
            .NotEmpty()
            .When(request => !request.Accepted)
            .WithMessage("Say what is wrong with the document so it can be replaced.")
            .MaximumLength(2000);
    }
}

public sealed class CheckSubdomainRequestValidator : AbstractValidator<CheckSubdomainRequest>
{
    public CheckSubdomainRequestValidator() =>
        RuleFor(request => request.Subdomain)
            .NotEmpty().WithMessage("Enter a web address to check.")
            .MaximumLength(63);
}

public sealed class UpdateOrganisationSettingsRequestValidator
    : AbstractValidator<UpdateOrganisationSettingsRequest>
{
    public UpdateOrganisationSettingsRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);

        // Bounds only. The handler CLAMPS against the platform floor, so an Organisation can
        // tighten but never loosen - these ranges just catch nonsense before it gets there.
        RuleFor(request => request.MaximumFailedAccessAttempts)
            .InclusiveBetween(3, 10)
            .When(request => request.MaximumFailedAccessAttempts.HasValue)
            .WithMessage("Allow between 3 and 10 failed attempts.");

        RuleFor(request => request.LockoutDurationMinutes)
            .InclusiveBetween(1, 1440)
            .When(request => request.LockoutDurationMinutes.HasValue)
            .WithMessage("Lockout must be between 1 minute and 24 hours.");

        RuleFor(request => request.PasswordMinimumLength)
            .InclusiveBetween(8, 64)
            .When(request => request.PasswordMinimumLength.HasValue)
            .WithMessage("Require between 8 and 64 characters.");

        RuleFor(request => request.PasswordExpiryDays)
            .InclusiveBetween(0, 365)
            .When(request => request.PasswordExpiryDays.HasValue)
            .WithMessage("Password expiry must be between 0 (never) and 365 days.");

        RuleFor(request => request.SessionIdleTimeoutMinutes)
            .InclusiveBetween(5, 480)
            .When(request => request.SessionIdleTimeoutMinutes.HasValue)
            .WithMessage("Idle timeout must be between 5 minutes and 8 hours.");
    }
}

public sealed class UpdateBusinessUnitRequestValidator : AbstractValidator<UpdateBusinessUnitRequest>
{
    public UpdateBusinessUnitRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThan(0);
        RuleFor(request => request.Name).MaximumLength(200);
        RuleFor(request => request.LegalName).MaximumLength(250);
        RuleFor(request => request.LogoUrl).MaximumLength(500);
        RuleFor(request => request.Description).MaximumLength(2000);

        RuleFor(request => request.ContactEmail)
            .Must(value => EmailValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.ContactEmail))
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request.SupportEmail)
            .Must(value => EmailValue.TryParse(value) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.SupportEmail))
            .WithMessage("That e-mail address is not valid.");

        RuleFor(request => request.MaximumTenants)
            .GreaterThan(0)
            .When(request => request.MaximumTenants.HasValue)
            .WithMessage("The organisation limit must be at least 1.");
    }
}
