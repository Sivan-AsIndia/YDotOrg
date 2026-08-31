using System.Text.RegularExpressions;
using FluentValidation;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Validators;

/// <summary>
/// Shared rules for the five master validators.
///
/// The postal-code pattern check is the one worth explaining. The column holds a REGULAR
/// EXPRESSION that other code will later run against an address, so an unparsable pattern is
/// not a cosmetic problem - it is a stored exception waiting for the first person who types a
/// postcode. Compiling it once here, at the moment it is entered, turns that into a field
/// message on the form the operator is already looking at.
/// </summary>
internal static class MasterValidationRules
{
    /// <summary>How long a validation regex is given to prove itself before it is rejected.</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Whether a string is a regular expression this application can actually run.
    ///
    /// Catastrophic backtracking is the real risk with an operator-supplied pattern, so the
    /// constructed regex carries a match timeout - and a pattern that cannot even be
    /// constructed is refused outright rather than stored to fail later.
    /// </summary>
    internal static bool IsUsablePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsValidCode(string? candidate) => CodeValue.TryParse(candidate) is not null;

    internal static bool IsValidIso2(string? candidate) => IsoAlpha2Value.TryParse(candidate) is not null;

    internal static bool IsValidCurrencyCode(string? candidate) =>
        CurrencyCodeValue.TryParse(candidate) is not null;

    internal const string CodeMessage =
        "Use up to 50 upper-case letters, digits, underscores or hyphens.";

    internal const string PatternMessage =
        "That is not a valid regular expression, so it could not be used to check an address.";
}

// =====================================================================================
// Countries
// =====================================================================================

/// <summary>Validator for creating a country.</summary>
public sealed class CreateCountryRequestValidator : AbstractValidator<CreateCountryRequest>
{
    public CreateCountryRequestValidator()
    {
        RuleFor(request => request.CountryCode)
            .NotEmpty().WithMessage("Enter a country code.")
            .Must(MasterValidationRules.IsValidCode).WithMessage(MasterValidationRules.CodeMessage);

        RuleFor(request => request.CountryName)
            .NotEmpty().WithMessage("Enter the country name.")
            .MaximumLength(150);

        RuleFor(request => request.Iso2)
            .NotEmpty().WithMessage("Enter the ISO 3166-1 alpha-2 code.")
            .Must(MasterValidationRules.IsValidIso2)
            .WithMessage("ISO2 is exactly two letters, such as IN.");

        RuleFor(request => request.OfficialName).MaximumLength(200);

        RuleFor(request => request.Iso3)
            .Length(3).WithMessage("ISO3 is exactly three letters, such as IND.")
            .Matches("^[A-Za-z]{3}$").WithMessage("ISO3 contains letters only.")
            .When(request => !string.IsNullOrWhiteSpace(request.Iso3));

        RuleFor(request => request.NumericCode)
            .MaximumLength(10)
            .Matches(@"^\d+$").WithMessage("The numeric code contains digits only.")
            .When(request => !string.IsNullOrWhiteSpace(request.NumericCode));

        RuleFor(request => request.DefaultCurrencyCode)
            .Must(MasterValidationRules.IsValidCurrencyCode)
            .WithMessage("Use a three-letter currency code, such as INR.")
            .When(request => !string.IsNullOrWhiteSpace(request.DefaultCurrencyCode));

        RuleFor(request => request.PostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        RuleFor(request => request.PhoneCountryCode)
            .MaximumLength(10)
            .Matches(@"^\+\d{1,6}$")
            .WithMessage("Write the dialling code with its plus sign, such as +91.")
            .When(request => !string.IsNullOrWhiteSpace(request.PhoneCountryCode));

        RuleFor(request => request.Region)
            .IsInEnum().WithMessage("Choose a region from the list.")
            .When(request => request.Region.HasValue);

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

/// <summary>
/// Validator for editing a country.
///
/// Every rule is guarded with <c>When(... is not null)</c>, because on an update a null means
/// "leave it alone" rather than "clear it" - an unguarded NotEmpty here would make a partial
/// update impossible.
/// </summary>
public sealed class UpdateCountryRequestValidator : AbstractValidator<UpdateCountryRequest>
{
    public UpdateCountryRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.CountryName)
            .MaximumLength(150)
            .When(request => request.CountryName is not null);

        RuleFor(request => request.Iso2)
            .Must(MasterValidationRules.IsValidIso2)
            .WithMessage("ISO2 is exactly two letters, such as IN.")
            .When(request => !string.IsNullOrWhiteSpace(request.Iso2));

        RuleFor(request => request.Iso3)
            .Length(3).Matches("^[A-Za-z]{3}$")
            .WithMessage("ISO3 is exactly three letters, such as IND.")
            .When(request => !string.IsNullOrWhiteSpace(request.Iso3));

        RuleFor(request => request.NumericCode)
            .MaximumLength(10).Matches(@"^\d+$")
            .WithMessage("The numeric code contains digits only.")
            .When(request => !string.IsNullOrWhiteSpace(request.NumericCode));

        RuleFor(request => request.DefaultCurrencyCode)
            .Must(MasterValidationRules.IsValidCurrencyCode)
            .WithMessage("Use a three-letter currency code, such as INR.")
            .When(request => !string.IsNullOrWhiteSpace(request.DefaultCurrencyCode));

        RuleFor(request => request.PostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        RuleFor(request => request.PhoneCountryCode)
            .MaximumLength(10).Matches(@"^\+\d{1,6}$")
            .WithMessage("Write the dialling code with its plus sign, such as +91.")
            .When(request => !string.IsNullOrWhiteSpace(request.PhoneCountryCode));

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.")
            .When(request => request.SortOrder.HasValue);

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

// =====================================================================================
// States and provinces
// =====================================================================================

/// <summary>Validator for creating a state.</summary>
public sealed class CreateStateProvinceRequestValidator : AbstractValidator<CreateStateProvinceRequest>
{
    public CreateStateProvinceRequestValidator()
    {
        RuleFor(request => request.StateProvinceCode)
            .NotEmpty().WithMessage("Enter a state code.")
            .Must(MasterValidationRules.IsValidCode).WithMessage(MasterValidationRules.CodeMessage);

        RuleFor(request => request.StateProvinceName)
            .NotEmpty().WithMessage("Enter the state name.")
            .MaximumLength(150);

        RuleFor(request => request.CountryId)
            .NotEmpty().WithMessage("Choose the country this state belongs to.");

        RuleFor(request => request.DisplayName).MaximumLength(150);

        RuleFor(request => request.JurisdictionType).IsInEnum();

        // Only required for Other, and the field is meaningless for anything else - so the
        // rule is scoped rather than being a blanket NotEmpty that would block every ordinary
        // state.
        RuleFor(request => request.OtherJurisdictionType)
            .NotEmpty().WithMessage("Describe the jurisdiction type.")
            .MaximumLength(100)
            .When(request => request.JurisdictionType == JurisdictionType.Other);

        RuleFor(request => request.GstStateCode)
            .MaximumLength(10)
            .Matches(@"^\d{1,10}$").WithMessage("The GST state code contains digits only.")
            .When(request => !string.IsNullOrWhiteSpace(request.GstStateCode));

        RuleFor(request => request.StateTaxJurisdictionCode).MaximumLength(50);

        RuleFor(request => request.PostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        RuleFor(request => request.AddressFormatHint).MaximumLength(300);

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

/// <summary>Validator for editing a state.</summary>
public sealed class UpdateStateProvinceRequestValidator : AbstractValidator<UpdateStateProvinceRequest>
{
    public UpdateStateProvinceRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.StateProvinceName)
            .MaximumLength(150)
            .When(request => request.StateProvinceName is not null);

        RuleFor(request => request.DisplayName).MaximumLength(150);

        RuleFor(request => request.JurisdictionType)
            .IsInEnum()
            .When(request => request.JurisdictionType.HasValue);

        RuleFor(request => request.OtherJurisdictionType)
            .NotEmpty().WithMessage("Describe the jurisdiction type.")
            .MaximumLength(100)
            .When(request => request.JurisdictionType == JurisdictionType.Other);

        RuleFor(request => request.GstStateCode)
            .MaximumLength(10).Matches(@"^\d{1,10}$")
            .WithMessage("The GST state code contains digits only.")
            .When(request => !string.IsNullOrWhiteSpace(request.GstStateCode));

        RuleFor(request => request.StateTaxJurisdictionCode).MaximumLength(50);

        RuleFor(request => request.PostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        RuleFor(request => request.AddressFormatHint).MaximumLength(300);

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.")
            .When(request => request.SortOrder.HasValue);

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

// =====================================================================================
// Cities
// =====================================================================================

/// <summary>
/// Validator for creating a city.
///
/// THE COORDINATE RANGES ARE NOT CHECKED HERE, deliberately. The handler parses the pair
/// through <c>GeoCoordinateValue</c>, which distinguishes "out of range" from "only one of the
/// two was supplied" - and the second of those is a rule about the RELATIONSHIP between two
/// fields, which a per-field validator states poorly and a value object states once.
/// </summary>
public sealed class CreateCityRequestValidator : AbstractValidator<CreateCityRequest>
{
    public CreateCityRequestValidator()
    {
        RuleFor(request => request.CityCode)
            .NotEmpty().WithMessage("Enter a city code.")
            .Must(MasterValidationRules.IsValidCode).WithMessage(MasterValidationRules.CodeMessage);

        RuleFor(request => request.CityName)
            .NotEmpty().WithMessage("Enter the city name.")
            .MaximumLength(150);

        RuleFor(request => request.StateProvinceId)
            .NotEmpty().WithMessage("Choose the state this city belongs to.");

        RuleFor(request => request.DisplayName).MaximumLength(150);

        RuleFor(request => request.DefaultPostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

/// <summary>Validator for editing a city.</summary>
public sealed class UpdateCityRequestValidator : AbstractValidator<UpdateCityRequest>
{
    public UpdateCityRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.CityName)
            .MaximumLength(150)
            .When(request => request.CityName is not null);

        RuleFor(request => request.DisplayName).MaximumLength(150);

        RuleFor(request => request.DefaultPostalCodePattern)
            .MaximumLength(200)
            .Must(MasterValidationRules.IsUsablePattern)
            .WithMessage(MasterValidationRules.PatternMessage);

        // Sending coordinates AND asking for them to be cleared is contradictory, and
        // silently picking one would leave the operator unsure which won.
        RuleFor(request => request.ClearCoordinates)
            .Equal(false)
            .When(request => request.Latitude.HasValue || request.Longitude.HasValue)
            .WithMessage("Either supply coordinates or clear them, not both.");

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.")
            .When(request => request.SortOrder.HasValue);

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

// =====================================================================================
// Currencies
// =====================================================================================

/// <summary>Validator for creating a currency.</summary>
public sealed class CreateCurrencyRequestValidator : AbstractValidator<CreateCurrencyRequest>
{
    public CreateCurrencyRequestValidator()
    {
        RuleFor(request => request.CurrencyCode)
            .NotEmpty().WithMessage("Enter a currency code.")
            .Must(MasterValidationRules.IsValidCurrencyCode)
            .WithMessage("A currency code is exactly three letters, such as INR.");

        RuleFor(request => request.CurrencyName)
            .NotEmpty().WithMessage("Enter the currency name.")
            .MaximumLength(150);

        RuleFor(request => request.NumericCode)
            .InclusiveBetween(1, 999).WithMessage("The ISO numeric code runs from 1 to 999.")
            .When(request => request.NumericCode.HasValue);

        RuleFor(request => request.CurrencyType).IsInEnum();
        RuleFor(request => request.SymbolPosition).IsInEnum();
        RuleFor(request => request.RoundingMode).IsInEnum();

        RuleFor(request => request.Symbol).MaximumLength(10);

        RuleFor(request => request.DisplayFormat).MaximumLength(50);

        // Eight places is Bitcoin, which is the most any real unit subdivides into. A larger
        // value would exceed what the amount columns can represent anyway.
        RuleFor(request => request.DecimalPlaces)
            .InclusiveBetween(0, 8)
            .WithMessage("Decimal places run from 0 to 8.");

        RuleFor(request => request.MinorUnitName).MaximumLength(50);

        RuleFor(request => request.RoundingStep)
            .GreaterThan(0).WithMessage("The rounding step must be greater than zero.")
            .When(request => request.RoundingStep.HasValue);

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

/// <summary>Validator for editing a currency.</summary>
public sealed class UpdateCurrencyRequestValidator : AbstractValidator<UpdateCurrencyRequest>
{
    public UpdateCurrencyRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.CurrencyName)
            .MaximumLength(150)
            .When(request => request.CurrencyName is not null);

        RuleFor(request => request.NumericCode)
            .InclusiveBetween(1, 999).WithMessage("The ISO numeric code runs from 1 to 999.")
            .When(request => request.NumericCode.HasValue);

        RuleFor(request => request.Symbol).MaximumLength(10);
        RuleFor(request => request.DisplayFormat).MaximumLength(50);
        RuleFor(request => request.MinorUnitName).MaximumLength(50);

        RuleFor(request => request.DecimalPlaces)
            .InclusiveBetween(0, 8).WithMessage("Decimal places run from 0 to 8.")
            .When(request => request.DecimalPlaces.HasValue);

        RuleFor(request => request.RoundingStep)
            .GreaterThan(0).WithMessage("The rounding step must be greater than zero.")
            .When(request => request.RoundingStep.HasValue);

        RuleFor(request => request.ClearRoundingStep)
            .Equal(false)
            .When(request => request.RoundingStep.HasValue)
            .WithMessage("Either set a rounding step or clear it, not both.");

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.")
            .When(request => request.SortOrder.HasValue);

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

// =====================================================================================
// Time zones
// =====================================================================================

/// <summary>Validator for creating a time zone.</summary>
public sealed class CreateTimeZoneRequestValidator : AbstractValidator<CreateTimeZoneRequest>
{
    public CreateTimeZoneRequestValidator()
    {
        RuleFor(request => request.TimeZoneKey)
            .NotEmpty().WithMessage("Enter the IANA time-zone key.")
            .MaximumLength(100)
            .Matches(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+)*$")
            .WithMessage("Use an IANA identifier, such as Asia/Kolkata or UTC.");

        RuleFor(request => request.DisplayName)
            .NotEmpty().WithMessage("Enter the display name.")
            .MaximumLength(150);

        RuleFor(request => request.ShortName)
            .MaximumLength(10)
            .When(request => !string.IsNullOrWhiteSpace(request.ShortName));

        // The real-world range. Kiritimati is +14:00 and Baker Island -12:00; nothing sits
        // outside those.
        RuleFor(request => request.StandardUtcOffsetMinutes)
            .InclusiveBetween(-720, 840)
            .WithMessage("Offsets run from -720 minutes (-12:00) to +840 minutes (+14:00).");

        RuleFor(request => request.DaylightSavingRuleNote).MaximumLength(500);

        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

/// <summary>Validator for editing a time zone.</summary>
public sealed class UpdateTimeZoneRequestValidator : AbstractValidator<UpdateTimeZoneRequest>
{
    public UpdateTimeZoneRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.DisplayName)
            .MaximumLength(150)
            .When(request => request.DisplayName is not null);

        RuleFor(request => request.ShortName)
            .MaximumLength(10)
            .When(request => !string.IsNullOrWhiteSpace(request.ShortName));

        RuleFor(request => request.StandardUtcOffsetMinutes)
            .InclusiveBetween(-720, 840)
            .WithMessage("Offsets run from -720 minutes (-12:00) to +840 minutes (+14:00).")
            .When(request => request.StandardUtcOffsetMinutes.HasValue);

        RuleFor(request => request.DaylightSavingRuleNote).MaximumLength(500);

        RuleFor(request => request.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.")
            .When(request => request.SortOrder.HasValue);

        RuleFor(request => request.Notes).MaximumLength(1000);
    }
}

// =====================================================================================
// Shared commands
// =====================================================================================

/// <summary>Validator for an activate/deactivate request.</summary>
public sealed class ChangeMasterStatusRequestValidator : AbstractValidator<ChangeMasterStatusRequest>
{
    public ChangeMasterStatusRequestValidator()
    {
        RuleFor(request => request.Status).IsInEnum();

        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

/// <summary>Validator for the body of an activate/deactivate call.</summary>
public sealed class MasterStatusChangeRequestValidator : AbstractValidator<MasterStatusChangeRequest>
{
    public MasterStatusChangeRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

/// <summary>Validator for a delete request.</summary>
public sealed class DeleteMasterRequestValidator : AbstractValidator<DeleteMasterRequest>
{
    public DeleteMasterRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0).WithMessage("The record version is missing. Reload the page and try again.");

        RuleFor(request => request.Reason).MaximumLength(500);
    }
}
