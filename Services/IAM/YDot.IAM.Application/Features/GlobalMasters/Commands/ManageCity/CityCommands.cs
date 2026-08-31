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

namespace YDot.IAM.Application.Features.GlobalMasters.Commands.ManageCity;

/// <summary>Creates a city beneath a state.</summary>
public sealed record CreateCityCommand(CreateCityRequest Request);

/// <summary>Edits a city.</summary>
public sealed record UpdateCityCommand(Guid CityId, UpdateCityRequest Request);

/// <summary>Activates or deactivates a city.</summary>
public sealed record ChangeCityStatusCommand(Guid CityId, ChangeMasterStatusRequest Request);

/// <summary>Deletes a city. Nothing hangs beneath one, so this always succeeds for an owned row.</summary>
public sealed record DeleteCityCommand(Guid CityId, DeleteMasterRequest Request);

/// <summary>
/// City maintenance.
///
/// THE COUNTRY IS DERIVED FROM THE STATE, and that is the single most important line in this
/// file. <c>City.CountryId</c> is denormalised so a country-level report does not have to join
/// through the state table, and a denormalised column is only safe while there is no way to
/// author it into disagreement with its source. The create request has no CountryId field, the
/// mapper takes it from the loaded state, and the update request cannot re-parent - which
/// closes every route by which the two could drift apart.
///
/// COORDINATES ARE PARSED, NOT RANGE-CHECKED IN PASSING. A latitude of 91 is almost always a
/// transposed pair rather than a distant place, and <c>GeoCoordinateValue</c> reports that as
/// a distinct outcome so the message can say so.
/// </summary>
public sealed class CityCommandHandler(
    IGlobalMasterRepository masters,
    IAuditService audit,
    GlobalMasterWriteGuard guard,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CityDetailResponse>> HandleAsync(
        CreateCityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var scopeTenantId = guard.WriteScopeTenantId;

        var code = CodeValue.TryParse(request.CityCode)?.Value;
        if (code is null)
        {
            return Result.Failure<CityDetailResponse>(Error.Validation(
                "That city code is not valid.",
                [new ValidationError(
                    nameof(request.CityCode),
                    "Use upper-case letters, digits, underscores or hyphens.")]));
        }

        var state = await masters.GetStateProvinceAsync(request.StateProvinceId, cancellationToken);
        if (state is null)
        {
            return Result.Failure<CityDetailResponse>(Error.NotFound("That state was not found."));
        }

        var country = await masters.GetCountryAsync(state.CountryId, cancellationToken);
        if (country is null)
        {
            // Reachable only if a state outlived its country, which the delete guard prevents.
            // Reported as a dependency failure rather than a not-found, because the CALLER did
            // nothing wrong: the catalogue is inconsistent and somebody needs to know.
            return Result.Failure<CityDetailResponse>(Error.Dependency(
                "That state is not linked to a country. The master catalogue needs attention."));
        }

        if (await masters.CodeExistsAsync<City>(code, scopeTenantId, null, cancellationToken))
        {
            return Result.Failure<CityDetailResponse>(
                Error.Duplicate($"A city with code {code} already exists in this catalogue."));
        }

        var coordinates = ParseCoordinates(request.Latitude, request.Longitude);
        if (coordinates.IsFailure)
        {
            return Result.Failure<CityDetailResponse>(coordinates.Error!);
        }

        var city = request.ToEntity(state, coordinates.Value, scopeTenantId, guard.BusinessUnitId);

        await masters.AddAsync(city, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterCreated,
            nameof(City),
            city.Id,
            city.Name,
            new
            {
                city.Code,
                State = state.Code,
                Country = country.Code,
                Scope = scopeTenantId is null ? "Platform" : "Organisation"
            },
            cancellationToken: cancellationToken);

        return city.ToDetailResponse(
            state.Code, state.Name, country.Code, country.Name, guard.IsSuperAdmin);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateCityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var city = await masters.GetCityAsync(command.CityId, cancellationToken);
        if (city is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That city was not found."));
        }

        var guarded = GuardWrite(city, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        var coordinates = ParseCoordinates(request.Latitude, request.Longitude);
        if (coordinates.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(coordinates.Error!);
        }

        request.ApplyTo(city, coordinates.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterUpdated,
            nameof(City),
            city.Id,
            city.Name,
            new { city.Code },
            cancellationToken: cancellationToken);

        return BuildOutcome(city, "City updated.");
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeCityStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var city = await masters.GetCityAsync(command.CityId, cancellationToken);
        if (city is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That city was not found."));
        }

        var guarded = GuardWrite(city, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        if (city.Status == request.Status)
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition($"That city is already {request.Status}."));
        }

        city.Status = request.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.Status == MasterDataStatus.Active
                ? AuditActionCodes.GlobalMasterActivated
                : AuditActionCodes.GlobalMasterDeactivated,
            nameof(City),
            city.Id,
            city.Name,
            new { city.Code, NewStatus = request.Status.ToString() },
            request.Reason,
            cancellationToken);

        return BuildOutcome(
            city, request.Status == MasterDataStatus.Active ? "City activated." : "City deactivated.");
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteCityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var city = await masters.GetCityAsync(command.CityId, cancellationToken);
        if (city is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That city was not found."));
        }

        var guarded = GuardWrite(city, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        // NO DEPENDENCY CHECK, and deliberately not one. A city is the leaf of the geography:
        // nothing in this catalogue hangs beneath it. Rows in OTHER modules may reference it,
        // and those are protected by their own foreign keys rather than by a count here - a
        // check in this handler could only ever cover the tables IAM owns and would give a
        // false sense of completeness for the ones it does not.
        var snapshot = new { city.Code, city.Name, city.StateProvinceId, city.CountryId };

        masters.Remove(city);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterDeleted,
            nameof(City),
            city.Id,
            city.Name,
            snapshot,
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(city.Id, city.Status.ToString(), city.Version, "City deleted.", []);
    }

    /// <summary>The ownership and version checks, in the order every write path needs them.</summary>
    private Result GuardWrite(City city, long expectedVersion)
    {
        var writable = guard.EnsureWritable(city, $"The city {city.Name}");

        return writable.IsFailure
            ? writable
            : GlobalMasterWriteGuard.EnsureVersionMatches(city, expectedVersion);
    }

    /// <summary>
    /// Turns a latitude/longitude pair into a value object, or into the field message that
    /// explains what is wrong with it.
    ///
    /// A successful result with a null Value means "none supplied", which is a legitimate
    /// answer and not an error - a city that has not been geocoded yet is an ordinary row.
    /// </summary>
    private static Result<GeoCoordinateValue?> ParseCoordinates(decimal? latitude, decimal? longitude)
    {
        var parsed = GeoCoordinateValue.TryParse(latitude, longitude);

        return parsed.Outcome switch
        {
            GeoCoordinateOutcome.NotSupplied => Result.Success<GeoCoordinateValue?>(null),
            GeoCoordinateOutcome.Parsed => Result.Success<GeoCoordinateValue?>(parsed.Value),

            GeoCoordinateOutcome.Incomplete => Result.Failure<GeoCoordinateValue?>(Error.Validation(
                "A location needs both a latitude and a longitude.",
                [new ValidationError(
                    latitude is null ? "latitude" : "longitude",
                    "Supply both coordinates, or neither.")])),

            GeoCoordinateOutcome.LatitudeOutOfRange => Result.Failure<GeoCoordinateValue?>(Error.Validation(
                "That latitude is out of range.",
                [new ValidationError(
                    "latitude",
                    "Latitude runs from -90 to 90. Check the two values are not the wrong way round.")])),

            _ => Result.Failure<GeoCoordinateValue?>(Error.Validation(
                "That longitude is out of range.",
                [new ValidationError(
                    "longitude",
                    "Longitude runs from -180 to 180. Check the two values are not the wrong way round.")]))
        };
    }

    /// <summary>
    /// A city has no dependents, so the permitted-action list is built with a count of zero
    /// rather than a query - unlike the other four masters, where it has to be read.
    /// </summary>
    private OutcomeResponse BuildOutcome(City city, string message) =>
        new(city.Id,
            city.Status.ToString(),
            city.Version,
            message,
            GlobalMasterMappingConfig.PermittedActionsFor(city, guard.IsSuperAdmin, dependentCount: 0));
}
