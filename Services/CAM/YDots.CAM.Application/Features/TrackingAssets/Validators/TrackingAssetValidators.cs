using FluentValidation;
using YDots.CAM.Application.Features.TrackingAssets.DTOs;

namespace YDots.CAM.Application.Features.TrackingAssets.Validators;

/// <summary>
/// Validators for the Tracking Assets slice.
///
/// THE DESTINATION IS CHECKED AS AN ABSOLUTE HTTP URL, which the original never did. A
/// destination is what a QR code resolves to; a relative path or a <c>javascript:</c> string
/// stored here would either produce a dead scan or, worse, a link that runs script in whatever
/// renders it.
/// </summary>
public sealed class CreateTrackingAssetRequestValidator : AbstractValidator<CreateTrackingAssetRequest>
{
    public CreateTrackingAssetRequestValidator()
    {
        RuleFor(request => request.CampaignId)
            .NotEmpty().WithMessage("Choose the campaign this asset belongs to.");

        RuleFor(request => request.AssetType).IsInEnum();

        RuleFor(request => request.ChannelId)
            .NotEmpty().WithMessage("Choose a channel.");

        RuleFor(request => request.SourceId)
            .NotEmpty().WithMessage("Choose a source.");

        RuleFor(request => request.MediumId)
            .NotEmpty().WithMessage("Choose a medium.");

        RuleFor(request => request.Destination)
            .NotEmpty().WithMessage("Enter the destination this asset points to.")
            .MaximumLength(2000)
            .Must(TrackingAssetValidationRules.IsAbsoluteHttpUrl)
            .WithMessage("Enter a full web address beginning http:// or https://.");

        RuleFor(request => request.ContentTag)
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9_.-]+$")
            .WithMessage("A content tag may contain letters, digits, dots, hyphens or underscores.")
            .When(request => !string.IsNullOrWhiteSpace(request.ContentTag));

        RuleFor(request => request.ActiveTo)
            .GreaterThan(request => request.ActiveFrom)
            .WithMessage("The end of the active window must be after its start.");

        // MANDATORY, AND ONLY DRAFT OR SUBMITTED. The Generate asset form asks for a status and
        // the module brief lists it among the required fields, so it is no longer defaulted to
        // Draft on the contract - a request that omits it now fails the enum check here rather
        // than quietly producing a Draft nobody chose. Everything past Submitted has its own
        // endpoint, permission and rules.
        RuleFor(request => request.Status)
            .IsInEnum().WithMessage("Choose the asset status.")
            .Must(status => status is Domain.Enums.TrackingAssetStatus.Draft
                or Domain.Enums.TrackingAssetStatus.Submitted)
            .WithMessage("A tracking asset can only be created as Draft or Submitted.");

        RuleFor(request => request.ActiveFrom)
            .NotEqual(default(DateTimeOffset)).WithMessage("Choose when this asset becomes active.");

        RuleFor(request => request.ActiveTo)
            .NotEqual(default(DateTimeOffset)).WithMessage("Choose when this asset stops being active.");

        RuleForEach(request => request.Places).SetValidator(new TrackingAssetPlaceRequestValidator());
    }
}

/// <summary>Validator for editing a tracking asset.</summary>
public sealed class UpdateTrackingAssetRequestValidator : AbstractValidator<UpdateTrackingAssetRequest>
{
    public UpdateTrackingAssetRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.AssetType).IsInEnum();
        RuleFor(request => request.ChannelId).NotEmpty().WithMessage("Choose a channel.");
        RuleFor(request => request.SourceId).NotEmpty().WithMessage("Choose a source.");
        RuleFor(request => request.MediumId).NotEmpty().WithMessage("Choose a medium.");

        RuleFor(request => request.Destination)
            .NotEmpty().WithMessage("Enter the destination this asset points to.")
            .MaximumLength(2000)
            .Must(TrackingAssetValidationRules.IsAbsoluteHttpUrl)
            .WithMessage("Enter a full web address beginning http:// or https://.");

        RuleFor(request => request.ContentTag)
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9_.-]+$")
            .WithMessage("A content tag may contain letters, digits, dots, hyphens or underscores.")
            .When(request => !string.IsNullOrWhiteSpace(request.ContentTag));

        RuleFor(request => request.ActiveTo)
            .GreaterThan(request => request.ActiveFrom)
            .WithMessage("The end of the active window must be after its start.");

        RuleForEach(request => request.Places).SetValidator(new TrackingAssetPlaceRequestValidator());
    }
}

/// <summary>Validator for one placement.</summary>
public sealed class TrackingAssetPlaceRequestValidator : AbstractValidator<TrackingAssetPlaceRequest>
{
    public TrackingAssetPlaceRequestValidator()
    {
        RuleFor(place => place.PlaceName)
            .NotEmpty().WithMessage("Name the placement.")
            .MaximumLength(200);

        RuleFor(place => place.Destination)
            .NotEmpty().WithMessage("Enter the destination for this placement.")
            .MaximumLength(2000)
            .Must(TrackingAssetValidationRules.IsAbsoluteHttpUrl)
            .WithMessage("Enter a full web address beginning http:// or https://.");

        RuleForEach(place => place.CustomFields)
            .SetValidator(new TrackingAssetCustomFieldRequestValidator());
    }
}

/// <summary>Validator for one custom field.</summary>
public sealed class TrackingAssetCustomFieldRequestValidator
    : AbstractValidator<TrackingAssetCustomFieldRequest>
{
    public TrackingAssetCustomFieldRequestValidator()
    {
        RuleFor(field => field.FieldName)
            .NotEmpty().WithMessage("Name the field.")
            .MaximumLength(100);

        RuleFor(field => field.Value).MaximumLength(500);
    }
}

/// <summary>Validator for a tracking asset lifecycle transition body.</summary>
public sealed class TrackingAssetLifecycleRequestValidator
    : AbstractValidator<TrackingAssetLifecycleRequest>
{
    public TrackingAssetLifecycleRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason).MaximumLength(2000);
    }
}

/// <summary>The rules the tracking asset validators share.</summary>
internal static class TrackingAssetValidationRules
{
    /// <summary>
    /// Whether a string is an absolute http or https URL.
    ///
    /// THE SCHEME CHECK IS THE POINT, not the parse. <c>Uri.TryCreate</c> alone accepts
    /// <c>javascript:</c>, <c>file:</c> and <c>data:</c>, any of which stored as a destination
    /// would be a link the application then hands to a browser.
    /// </summary>
    internal static bool IsAbsoluteHttpUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
