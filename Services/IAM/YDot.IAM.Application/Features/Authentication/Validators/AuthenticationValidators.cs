using FluentValidation;
using YDot.IAM.Application.Features.Authentication.DTOs;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Authentication.Validators;

/// <summary>
/// Validators for the authentication slice.
///
/// A DELIBERATE LINE ABOUT WHAT IS NOT VALIDATED HERE. The sign-in validator checks that the
/// fields are PRESENT and of a plausible shape. It does not check password complexity, and it
/// must not: a sign-in is checking an EXISTING password, and refusing it at the validator for
/// being too short would tell the caller something about the stored password, as well as
/// locking out every account created before a policy change.
///
/// Complexity is enforced where a password is SET — invitation acceptance, reset and change —
/// and there it comes from <c>IPasswordHasher.ValidatePolicy</c> rather than being duplicated
/// here, so the rule lives in exactly one place and honours the Organisation override.
/// </summary>
public sealed class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    public SignInRequestValidator()
    {
        RuleFor(request => request.Identifier)
            .NotEmpty().WithMessage("Enter your e-mail address or username.")
            .MaximumLength(320).WithMessage("That e-mail address or username is too long.");

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Enter your password.")
            .MaximumLength(256).WithMessage("That password is too long.");

        RuleFor(request => request.DeviceIdentifier)
            .MaximumLength(200)
            .When(request => !string.IsNullOrWhiteSpace(request.DeviceIdentifier));

        RuleFor(request => request.DeviceName)
            .MaximumLength(160)
            .When(request => !string.IsNullOrWhiteSpace(request.DeviceName));
    }
}

public sealed class VerifyMfaRequestValidator : AbstractValidator<VerifyMfaRequest>
{
    public VerifyMfaRequestValidator()
    {
        RuleFor(request => request.ChallengeToken)
            .NotEmpty().WithMessage("That verification session has ended. Sign in again.");

        RuleFor(request => request.Code)
            .NotEmpty().WithMessage("Enter the verification code.")
            .Matches("^[0-9]{4,10}$").WithMessage("The verification code is digits only.");
    }
}

public sealed class ResendMfaChallengeRequestValidator : AbstractValidator<ResendMfaChallengeRequest>
{
    public ResendMfaChallengeRequestValidator() =>
        RuleFor(request => request.ChallengeToken)
            .NotEmpty().WithMessage("That verification session has ended. Sign in again.");
}

public sealed class RedeemRecoveryCodeRequestValidator : AbstractValidator<RedeemRecoveryCodeRequest>
{
    public RedeemRecoveryCodeRequestValidator()
    {
        RuleFor(request => request.ChallengeToken)
            .NotEmpty().WithMessage("That verification session has ended. Sign in again.");

        RuleFor(request => request.RecoveryCode)
            .NotEmpty().WithMessage("Enter one of your recovery codes.")
            .MaximumLength(64);
    }
}

public sealed class SelectTenantRequestValidator : AbstractValidator<SelectTenantRequest>
{
    public SelectTenantRequestValidator() =>
        RuleFor(request => request.TenantId)
            .NotEmpty().WithMessage("Choose an organisation.");
}

public sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty().WithMessage("That invitation link is not valid.");

        // Length only. The real policy check runs in the handler, where the Organisation
        // override is known and every failure can be reported at once.
        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Choose a password.")
            .MaximumLength(128).WithMessage("That password is too long.");

        RuleFor(request => request.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm your password.")
            .Equal(request => request.Password).WithMessage("The passwords do not match.");

        RuleFor(request => request.FirstName)
            .MaximumLength(80)
            .When(request => !string.IsNullOrWhiteSpace(request.FirstName));

        RuleFor(request => request.LastName)
            .MaximumLength(80)
            .When(request => !string.IsNullOrWhiteSpace(request.LastName));

        RuleFor(request => request)
            .Must(request => MobileNumberValue.TryParse(request.MobileCountryCode, request.MobileNumber) is not null)
            .When(request => !string.IsNullOrWhiteSpace(request.MobileNumber))
            .WithName(nameof(AcceptInvitationRequest.MobileNumber))
            .WithMessage("Enter a valid mobile number with its country code.");

        RuleFor(request => request.AcceptTerms)
            .Equal(true).WithMessage("You must accept the terms to continue.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(request => request.Identifier)
            .NotEmpty().WithMessage("Enter your e-mail address or username.")
            .MaximumLength(320);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty().WithMessage("That reset link is not valid.");

        RuleFor(request => request.Password)
            .NotEmpty().WithMessage("Choose a new password.")
            .MaximumLength(128);

        RuleFor(request => request.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm your new password.")
            .Equal(request => request.Password).WithMessage("The passwords do not match.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().WithMessage("Enter your current password.");

        RuleFor(request => request.NewPassword)
            .NotEmpty().WithMessage("Choose a new password.")
            .MaximumLength(128)
            .NotEqual(request => request.CurrentPassword)
            .WithMessage("Your new password must be different from your current one.");

        RuleFor(request => request.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm your new password.")
            .Equal(request => request.NewPassword).WithMessage("The passwords do not match.");
    }
}

public sealed class ReauthenticateRequestValidator : AbstractValidator<ReauthenticateRequest>
{
    public ReauthenticateRequestValidator() =>
        // One or the other, not neither. Which one is up to whoever is at the keyboard and
        // what they happen to have to hand.
        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.Password)
                             || !string.IsNullOrWhiteSpace(request.MfaCode))
            .WithName(nameof(ReauthenticateRequest.Password))
            .WithMessage("Enter your password or a verification code.");
}

public sealed class RevokeSessionRequestValidator : AbstractValidator<RevokeSessionRequest>
{
    public RevokeSessionRequestValidator()
    {
        RuleFor(request => request.SessionId)
            .NotEmpty().WithMessage("Choose a session to end.");

        RuleFor(request => request.Reason)
            .MaximumLength(300)
            .When(request => !string.IsNullOrWhiteSpace(request.Reason));
    }
}

public sealed class ResendInvitationRequestValidator : AbstractValidator<ResendInvitationRequest>
{
    public ResendInvitationRequestValidator()
    {
        RuleFor(request => request.UserId)
            .NotEmpty().WithMessage("Choose a user.");

        RuleFor(request => request.Message)
            .MaximumLength(1000)
            .When(request => !string.IsNullOrWhiteSpace(request.Message));
    }
}
