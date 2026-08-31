using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCountry;

/// <summary>Creates a country in the caller's scope.</summary>
public sealed record CreateCountryCommand(CreateCountryRequest Request);

/// <summary>Edits a country.</summary>
public sealed record UpdateCountryCommand(Guid CountryId, UpdateCountryRequest Request);

/// <summary>Activates or deactivates a country.</summary>
public sealed record ChangeCountryStatusCommand(Guid CountryId, ChangeMasterStatusRequest Request);

/// <summary>Deletes a country. Refused while any state or city sits beneath it.</summary>
public sealed record DeleteCountryCommand(Guid CountryId, DeleteMasterRequest Request);

/// <summary>
/// Country maintenance.
///
/// THE TWO RULES THAT MATTER HERE ARE SCOPE AND UNIQUENESS, and they interact.
///
/// A row lands in whichever Organisation the caller is operating in, or in the shared
/// platform catalogue when a root user is operating in none - <see cref="GlobalMasterWriteGuard"/>
/// owns that decision. Uniqueness is then checked WITHIN that scope, so TEN001 defining its
/// own country coded IN does not collide with the platform's IN, but TEN001 defining a second
/// IN of its own does.
///
/// That is the whole reason <c>CodeExistsAsync</c> takes the target scope rather than
/// filtering on what the caller can see: what the caller can SEE is platform plus their own,
/// and checking uniqueness against that union would wrongly refuse the first of those two
/// cases.
/// </summary>
public sealed class CountryCommandHandler(
    IGlobalMasterRepository masters,
    IAuditService audit,
    GlobalMasterWriteGuard guard,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CountryDetailResponse>> HandleAsync(
        CreateCountryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var scopeTenantId = guard.WriteScopeTenantId;

        var code = CodeValue.TryParse(request.CountryCode)?.Value;
        if (code is null)
        {
            return Result.Failure<CountryDetailResponse>(Error.Validation(
                "That country code is not valid.",
                [new ValidationError(
                    nameof(request.CountryCode),
                    "Use upper-case letters, digits, underscores or hyphens.")]));
        }

        var iso2 = IsoAlpha2Value.TryParse(request.Iso2)?.Value;
        if (iso2 is null)
        {
            return Result.Failure<CountryDetailResponse>(Error.Validation(
                "That ISO code is not valid.",
                [new ValidationError(nameof(request.Iso2), "ISO2 must be exactly two letters.")]));
        }

        if (await masters.CodeExistsAsync<Domain.Entities.Country>(
                code, scopeTenantId, null, cancellationToken))
        {
            return Result.Failure<CountryDetailResponse>(
                Error.Duplicate($"A country with code {code} already exists in this catalogue."));
        }

        if (await masters.Iso2ExistsAsync(iso2, scopeTenantId, null, cancellationToken))
        {
            return Result.Failure<CountryDetailResponse>(
                Error.Duplicate($"A country with ISO code {iso2} already exists in this catalogue."));
        }

        var country = request.ToEntity(scopeTenantId, guard.BusinessUnitId);

        await masters.AddAsync(country, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterCreated,
            nameof(Domain.Entities.Country),
            country.Id,
            country.Name,
            new { country.Code, country.Iso2, Scope = scopeTenantId is null ? "Platform" : "Organisation" },
            cancellationToken: cancellationToken);

        // A brand-new country has nothing beneath it, so both counts are zero rather than
        // being queried for.
        return country.ToDetailResponse(
            stateProvinceCount: 0, cityCount: 0, isSuperAdmin: guard.IsSuperAdmin);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateCountryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var country = await masters.GetCountryAsync(command.CountryId, cancellationToken);
        if (country is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That country was not found."));
        }

        var writable = guard.EnsureWritable(country, $"The country {country.Name}");
        if (writable.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(writable.Error!);
        }

        var versioned = GlobalMasterWriteGuard.EnsureVersionMatches(country, request.ExpectedVersion);
        if (versioned.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(versioned.Error!);
        }

        // Checked BEFORE the mapper runs, so a rejected ISO change never reaches the entity.
        if (!string.IsNullOrWhiteSpace(request.Iso2))
        {
            var iso2 = IsoAlpha2Value.TryParse(request.Iso2)?.Value;
            if (iso2 is null)
            {
                return Result.Failure<OutcomeResponse>(Error.Validation(
                    "That ISO code is not valid.",
                    [new ValidationError(nameof(request.Iso2), "ISO2 must be exactly two letters.")]));
            }

            if (!string.Equals(iso2, country.Iso2, StringComparison.Ordinal)
                && await masters.Iso2ExistsAsync(iso2, country.TenantId, country.Id, cancellationToken))
            {
                return Result.Failure<OutcomeResponse>(
                    Error.Duplicate($"A country with ISO code {iso2} already exists in this catalogue."));
            }
        }

        request.ApplyTo(country);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterUpdated,
            nameof(Domain.Entities.Country),
            country.Id,
            country.Name,
            new { country.Code, country.Iso2 },
            cancellationToken: cancellationToken);

        return await BuildOutcomeAsync(country, "Country updated.", cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeCountryStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var country = await masters.GetCountryAsync(command.CountryId, cancellationToken);
        if (country is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That country was not found."));
        }

        var writable = guard.EnsureWritable(country, $"The country {country.Name}");
        if (writable.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(writable.Error!);
        }

        var versioned = GlobalMasterWriteGuard.EnsureVersionMatches(country, request.ExpectedVersion);
        if (versioned.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(versioned.Error!);
        }

        if (country.Status == request.Status)
        {
            return Result.Failure<OutcomeResponse>(Error.InvalidTransition(
                $"That country is already {request.Status}."));
        }

        country.Status = request.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.Status == MasterDataStatus.Active
                ? AuditActionCodes.GlobalMasterActivated
                : AuditActionCodes.GlobalMasterDeactivated,
            nameof(Domain.Entities.Country),
            country.Id,
            country.Name,
            new { country.Code, NewStatus = request.Status.ToString() },
            request.Reason,
            cancellationToken);

        return await BuildOutcomeAsync(
            country,
            request.Status == MasterDataStatus.Active ? "Country activated." : "Country deactivated.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteCountryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var country = await masters.GetCountryAsync(command.CountryId, cancellationToken);
        if (country is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That country was not found."));
        }

        var writable = guard.EnsureWritable(country, $"The country {country.Name}");
        if (writable.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(writable.Error!);
        }

        var versioned = GlobalMasterWriteGuard.EnsureVersionMatches(country, request.ExpectedVersion);
        if (versioned.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(versioned.Error!);
        }

        // STATES AND CITIES ARE COUNTED SEPARATELY so the message can name what is actually in
        // the way. A country can perfectly well have cities recorded against it with no states
        // in between - HasStates is false for Singapore - so counting only states would let
        // that delete through and orphan every city underneath.
        var stateCount = await masters.CountStatesForCountryAsync(country.Id, cancellationToken);
        var cityCount = await masters.CountCitiesForCountryAsync(country.Id, cancellationToken);

        var dependencyLabel = stateCount > 0 ? "states or provinces" : "cities";

        var free = GlobalMasterWriteGuard.EnsureNoDependents(
            stateCount + cityCount, $"The country {country.Name}", dependencyLabel);

        if (free.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(free.Error!);
        }

        var snapshot = new { country.Code, country.Iso2, country.Name };

        masters.Remove(country);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterDeleted,
            nameof(Domain.Entities.Country),
            country.Id,
            country.Name,
            snapshot,
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            country.Id, country.Status.ToString(), country.Version, "Country deleted.", []);
    }

    /// <summary>
    /// The answer a state-changing action returns.
    ///
    /// It carries the NEW version, which is what lets the screen issue a second edit without
    /// re-fetching - and without that, every second save on an open form would answer 409.
    ///
    /// THE DEPENDENT COUNTS ARE RE-READ rather than assumed to be zero. The permitted-action
    /// list is what the screen draws its buttons from, so claiming Delete is available on a
    /// country that has thirty states beneath it would produce a button that exists only to
    /// answer 409. Two COUNT queries on a save the operator explicitly asked for is a fair
    /// price for a toolbar that tells the truth.
    /// </summary>
    private async Task<OutcomeResponse> BuildOutcomeAsync(
        Domain.Entities.Country country, string message, CancellationToken cancellationToken)
    {
        var stateCount = await masters.CountStatesForCountryAsync(country.Id, cancellationToken);
        var cityCount = await masters.CountCitiesForCountryAsync(country.Id, cancellationToken);

        return new OutcomeResponse(
            country.Id,
            country.Status.ToString(),
            country.Version,
            message,
            GlobalMasterMappingConfig.PermittedActionsFor(
                country, guard.IsSuperAdmin, stateCount + cityCount));
    }
}
