using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCurrency;

/// <summary>Creates a currency.</summary>
public sealed record CreateCurrencyCommand(CreateCurrencyRequest Request);

/// <summary>Edits a currency.</summary>
public sealed record UpdateCurrencyCommand(Guid CurrencyId, UpdateCurrencyRequest Request);

/// <summary>Activates or deactivates a currency.</summary>
public sealed record ChangeCurrencyStatusCommand(Guid CurrencyId, ChangeMasterStatusRequest Request);

/// <summary>Deletes a currency. Refused while any country names it as their default.</summary>
public sealed record DeleteCurrencyCommand(Guid CurrencyId, DeleteMasterRequest Request);

/// <summary>
/// Currency maintenance.
///
/// THE DECIMAL PLACES ARE THE FIELD TO BE CAREFUL WITH. Editing them on a live currency
/// changes how every existing amount is rendered and rounded, which is why the update path
/// goes through the same version check as everything else and why the change is audited with
/// the old value alongside the new. It is not blocked - a currency genuinely set up wrongly
/// has to be correctable - but it is recorded in a way somebody can find later.
///
/// DELETION IS BLOCKED BY USAGE, and the usage is counted through <c>DefaultCurrencyCode</c>
/// on Country, which is a loose string reference rather than a foreign key. That means the
/// database will NOT stop the delete on its own, so the check here is the only thing standing
/// between a removed currency and a set of countries silently pointing at nothing.
/// </summary>
public sealed class CurrencyCommandHandler(
    IGlobalMasterRepository masters,
    IAuditService audit,
    GlobalMasterWriteGuard guard,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CurrencyDetailResponse>> HandleAsync(
        CreateCurrencyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var scopeTenantId = guard.WriteScopeTenantId;

        var code = CurrencyCodeValue.TryParse(request.CurrencyCode)?.Value;
        if (code is null)
        {
            return Result.Failure<CurrencyDetailResponse>(Error.Validation(
                "That currency code is not valid.",
                [new ValidationError(
                    nameof(request.CurrencyCode),
                    "A currency code is exactly three letters, such as INR.")]));
        }

        if (await masters.CodeExistsAsync<Currency>(code, scopeTenantId, null, cancellationToken))
        {
            return Result.Failure<CurrencyDetailResponse>(
                Error.Duplicate($"A currency with code {code} already exists in this catalogue."));
        }

        var currency = request.ToEntity(scopeTenantId, guard.BusinessUnitId);

        await masters.AddAsync(currency, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterCreated,
            nameof(Currency),
            currency.Id,
            currency.Name,
            new
            {
                currency.Code,
                currency.DecimalPlaces,
                Scope = scopeTenantId is null ? "Platform" : "Organisation"
            },
            cancellationToken: cancellationToken);

        return currency.ToDetailResponse(countryUsageCount: 0, isSuperAdmin: guard.IsSuperAdmin);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateCurrencyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var currency = await masters.GetCurrencyAsync(command.CurrencyId, cancellationToken);
        if (currency is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That currency was not found."));
        }

        var guarded = GuardWrite(currency, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        // Captured before the mapper runs, so the audit row can show what actually changed
        // rather than the new value twice.
        var previousDecimalPlaces = currency.DecimalPlaces;

        request.ApplyTo(currency);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterUpdated,
            nameof(Currency),
            currency.Id,
            currency.Name,
            new
            {
                currency.Code,
                PreviousDecimalPlaces = previousDecimalPlaces,
                currency.DecimalPlaces,
                DecimalPlacesChanged = previousDecimalPlaces != currency.DecimalPlaces
            },
            cancellationToken: cancellationToken);

        return await BuildOutcomeAsync(currency, "Currency updated.", cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeCurrencyStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var currency = await masters.GetCurrencyAsync(command.CurrencyId, cancellationToken);
        if (currency is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That currency was not found."));
        }

        var guarded = GuardWrite(currency, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        if (currency.Status == request.Status)
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition($"That currency is already {request.Status}."));
        }

        // DEACTIVATION IS NOT BLOCKED BY USAGE, unlike deletion, and the difference is the
        // point of having both. Retiring a currency should stop it appearing in new donation
        // forms while leaving every historic amount readable and every country that still
        // names it intact. Refusing here would leave an operator with no way to retire
        // anything that had ever been used.
        var usageCount = await masters.CountCountriesUsingCurrencyAsync(currency.Code, cancellationToken);

        currency.Status = request.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.Status == MasterDataStatus.Active
                ? AuditActionCodes.GlobalMasterActivated
                : AuditActionCodes.GlobalMasterDeactivated,
            nameof(Currency),
            currency.Id,
            currency.Name,
            new { currency.Code, NewStatus = request.Status.ToString(), CountriesAffected = usageCount },
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            currency.Id,
            currency.Status.ToString(),
            currency.Version,
            request.Status == MasterDataStatus.Active
                ? "Currency activated."
                : usageCount > 0
                    ? $"Currency deactivated. {usageCount} country/countries still name it as their default."
                    : "Currency deactivated.",
            GlobalMasterMappingConfig.PermittedActionsFor(currency, guard.IsSuperAdmin, usageCount));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteCurrencyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var currency = await masters.GetCurrencyAsync(command.CurrencyId, cancellationToken);
        if (currency is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That currency was not found."));
        }

        var guarded = GuardWrite(currency, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        var usageCount = await masters.CountCountriesUsingCurrencyAsync(currency.Code, cancellationToken);

        var free = GlobalMasterWriteGuard.EnsureNoDependents(
            usageCount, $"The currency {currency.Code}", "countries defaulting to it");

        if (free.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(free.Error!);
        }

        var snapshot = new { currency.Code, currency.Name, currency.DecimalPlaces };

        masters.Remove(currency);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterDeleted,
            nameof(Currency),
            currency.Id,
            currency.Name,
            snapshot,
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            currency.Id, currency.Status.ToString(), currency.Version, "Currency deleted.", []);
    }

    private Result GuardWrite(Currency currency, long expectedVersion)
    {
        var writable = guard.EnsureWritable(currency, $"The currency {currency.Code}");

        return writable.IsFailure
            ? writable
            : GlobalMasterWriteGuard.EnsureVersionMatches(currency, expectedVersion);
    }

    private async Task<OutcomeResponse> BuildOutcomeAsync(
        Currency currency, string message, CancellationToken cancellationToken)
    {
        var usageCount = await masters.CountCountriesUsingCurrencyAsync(currency.Code, cancellationToken);

        return new OutcomeResponse(
            currency.Id,
            currency.Status.ToString(),
            currency.Version,
            message,
            GlobalMasterMappingConfig.PermittedActionsFor(currency, guard.IsSuperAdmin, usageCount));
    }
}
