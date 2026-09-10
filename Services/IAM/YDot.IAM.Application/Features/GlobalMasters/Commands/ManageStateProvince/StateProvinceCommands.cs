using Microsoft.Extensions.Logging;
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

namespace YDot.IAM.Application.Features.GlobalMasters.Commands.ManageStateProvince;

/// <summary>Creates a state, province or union territory beneath a country.</summary>
public sealed record CreateStateProvinceCommand(CreateStateProvinceRequest Request);

/// <summary>Edits a state.</summary>
public sealed record UpdateStateProvinceCommand(Guid StateProvinceId, UpdateStateProvinceRequest Request);

/// <summary>Activates or deactivates a state.</summary>
public sealed record ChangeStateProvinceStatusCommand(Guid StateProvinceId, ChangeMasterStatusRequest Request);

/// <summary>Deletes a state. Refused while any city sits beneath it.</summary>
public sealed record DeleteStateProvinceCommand(Guid StateProvinceId, DeleteMasterRequest Request);

/// <summary>
/// State and province maintenance.
///
/// THE PARENT IS LOADED, NOT TRUSTED. The country arrives as an id on the request, and the
/// handler reads the row before using it. Under the scoped query filter that read returns the
/// platform catalogue plus the caller's own rows and nothing else, so a state can only ever be
/// attached to a country the caller was genuinely entitled to see - and an id belonging to
/// another Organisation comes back as "not found" rather than as a successful cross-Tenant
/// write.
///
/// A STATE MAY HANG OFF A PLATFORM COUNTRY. That combination is deliberate and it is the
/// common case: an Organisation adding a district of its own beneath the seeded India row is
/// exactly what the tenant overlay is for. What it may NOT do is edit that India row, which is
/// a different question and one <see cref="GlobalMasterWriteGuard"/> answers.
/// </summary>
public sealed class StateProvinceCommandHandler(
    IGlobalMasterRepository masters,
    IAuditService audit,
    GlobalMasterWriteGuard guard,
    IUnitOfWork unitOfWork,
    ILogger<StateProvinceCommandHandler> logger)
{
    public async Task<Result<StateProvinceDetailResponse>> HandleAsync(
        CreateStateProvinceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Creating state or province.");

        var request = command.Request;
        var scopeTenantId = guard.WriteScopeTenantId;

        var code = CodeValue.TryParse(request.StateProvinceCode)?.Value;
        if (code is null)
        {
            logger.LogWarning("State creation failed because the state code is invalid.");

            return Result.Failure<StateProvinceDetailResponse>(Error.Validation(
                "That state code is not valid.",
                [new ValidationError(
                    nameof(request.StateProvinceCode),
                    "Use upper-case letters, digits, underscores or hyphens.")]));
        }

        var country = await masters.GetCountryAsync(request.CountryId, cancellationToken);
        if (country is null)
        {
            logger.LogWarning(
                "State creation failed because the country was not found. CountryId: {CountryId}.",
                request.CountryId);

            return Result.Failure<StateProvinceDetailResponse>(
                Error.NotFound("That country was not found."));
        }

        if (await masters.CodeExistsAsync<StateProvince>(code, scopeTenantId, null, cancellationToken))
        {
            logger.LogWarning(
                "State creation failed because the state code already exists. Code: {Code}.",
                code);

            return Result.Failure<StateProvinceDetailResponse>(
                Error.Duplicate($"A state with code {code} already exists in this catalogue."));
        }

        var timeZoneName = await ResolveTimeZoneNameAsync(request.DefaultTimeZoneId, cancellationToken);
        if (request.DefaultTimeZoneId.HasValue && timeZoneName is null)
        {
            logger.LogWarning(
                "State creation failed because the specified time zone was not found. TimeZoneId: {TimeZoneId}.",
                request.DefaultTimeZoneId);

            return Result.Failure<StateProvinceDetailResponse>(
                Error.NotFound("That time zone was not found."));
        }

        var state = request.ToEntity(country, scopeTenantId, guard.BusinessUnitId);

        await masters.AddAsync(state, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterCreated,
            nameof(StateProvince),
            state.Id,
            state.Name,
            new { state.Code, Country = country.Code, Scope = scopeTenantId is null ? "Platform" : "Organisation" },
            cancellationToken: cancellationToken);

        logger.LogInformation("State created successfully. StateProvinceId: {StateProvinceId}, Code: {Code}, CountryId: {CountryId}.",
            state.Id,state.Code,state.CountryId);

        return state.ToDetailResponse(
            country.Code, country.Name, timeZoneName, cityCount: 0, isSuperAdmin: guard.IsSuperAdmin);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateStateProvinceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Updating state or province. StateProvinceId: {StateProvinceId}.",
            command.StateProvinceId);

        var request = command.Request;

        var state = await masters.GetStateProvinceAsync(command.StateProvinceId, cancellationToken);
        if (state is null)
        {
            logger.LogWarning("State update failed because the state was not found. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(Error.NotFound("That state was not found."));
        }

        var guarded = GuardWrite(state, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            logger.LogWarning( "State update rejected by the write guard. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        // Verified before the mapper runs, so a bad id never reaches the entity.
        if (request.DefaultTimeZoneId.HasValue
            && await ResolveTimeZoneNameAsync(request.DefaultTimeZoneId, cancellationToken) is null)
        {
            logger.LogWarning("State update failed because the specified time zone was not found. StateProvinceId: {StateProvinceId}, TimeZoneId: {TimeZoneId}.",
                command.StateProvinceId,request.DefaultTimeZoneId);

            return Result.Failure<OutcomeResponse>(Error.NotFound("That time zone was not found."));
        }

        request.ApplyTo(state);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterUpdated,
            nameof(StateProvince),
            state.Id,
            state.Name,
            new { state.Code },
            cancellationToken: cancellationToken);

        logger.LogInformation("State updated successfully. StateProvinceId: {StateProvinceId}, Code: {Code}.",
            state.Id,state.Code);

        return await BuildOutcomeAsync(state, "State updated.", cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeStateProvinceStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Changing state status. StateProvinceId: {StateProvinceId}, RequestedStatus: {RequestedStatus}.",
            command.StateProvinceId,command.Request.Status);

        var request = command.Request;

        var state = await masters.GetStateProvinceAsync(command.StateProvinceId, cancellationToken);
        if (state is null)
        {
            logger.LogWarning("State status change failed because the state was not found. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(Error.NotFound("That state was not found."));
        }

        var guarded = GuardWrite(state, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            logger.LogWarning("State status change rejected by the write guard. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        if (state.Status == request.Status)
        {
            logger.LogWarning("State status change rejected because the state is already in the requested status. StateProvinceId: {StateProvinceId}, Status: {Status}.",
                command.StateProvinceId,request.Status);

            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition($"That state is already {request.Status}."));
        }

        state.Status = request.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.Status == MasterDataStatus.Active
                ? AuditActionCodes.GlobalMasterActivated
                : AuditActionCodes.GlobalMasterDeactivated,
            nameof(StateProvince),
            state.Id,
            state.Name,
            new { state.Code, NewStatus = request.Status.ToString() },
            request.Reason,
            cancellationToken);

        logger.LogInformation("State status changed successfully. StateProvinceId: {StateProvinceId}, NewStatus: {NewStatus}.",
            state.Id,request.Status);

        return await BuildOutcomeAsync(
            state,
            request.Status == MasterDataStatus.Active ? "State activated." : "State deactivated.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteStateProvinceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation("Deleting state or province. StateProvinceId: {StateProvinceId}.",
            command.StateProvinceId);

        var request = command.Request;

        var state = await masters.GetStateProvinceAsync(command.StateProvinceId, cancellationToken);
        if (state is null)
        {
            logger.LogWarning("State deletion failed because the state was not found. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(Error.NotFound("That state was not found."));
        }

        var guarded = GuardWrite(state, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            logger.LogWarning("State deletion rejected by the write guard. StateProvinceId: {StateProvinceId}.",
                command.StateProvinceId);

            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        var cityCount = await masters.CountCitiesForStateAsync(state.Id, cancellationToken);

        var free = GlobalMasterWriteGuard.EnsureNoDependents(
            cityCount, $"The state {state.Name}", "cities");

        if (free.IsFailure)
        {
            logger.LogWarning("State deletion rejected because dependent cities exist. StateProvinceId: {StateProvinceId}, CityCount: {CityCount}.",
                command.StateProvinceId,cityCount);

            return Result.Failure<OutcomeResponse>(free.Error!);
        }

        var snapshot = new { state.Code, state.Name, state.CountryId };

        masters.Remove(state);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterDeleted,
            nameof(StateProvince),
            state.Id,
            state.Name,
            snapshot,
            request.Reason,
            cancellationToken);

        logger.LogInformation("State deleted successfully. StateProvinceId: {StateProvinceId}, Code: {Code}.",
            state.Id,state.Code);

        return new OutcomeResponse(
            state.Id, state.Status.ToString(), state.Version, "State deleted.", []);
    }

    /// <summary>The ownership and version checks, which every write path needs in the same order.</summary>
    private Result GuardWrite(StateProvince state, long expectedVersion)
    {
        var writable = guard.EnsureWritable(state, $"The state {state.Name}");

        if (writable.IsFailure)
        {
            logger.LogWarning("State write rejected by ownership or write guard. StateProvinceId: {StateProvinceId}.",
                state.Id);

            return writable;
        }

        var versioned = GlobalMasterWriteGuard.EnsureVersionMatches(state, expectedVersion);

        if (versioned.IsFailure)
        {
            logger.LogWarning("State write rejected because the entity version does not match. StateProvinceId: {StateProvinceId}.",
                state.Id);
        }

        return versioned;
    }

    /// <summary>
    /// The display name of a time zone, or null when the id names nothing the caller can see.
    ///
    /// Doubles as the existence check, which is why the caller tests the null rather than
    /// calling a separate <c>Exists</c> - one query answers both questions.
    /// </summary>
    private async Task<string?> ResolveTimeZoneNameAsync(Guid? timeZoneId, CancellationToken cancellationToken)
    {
        if (!timeZoneId.HasValue)
        {
            return null;
        }

        var timeZone = await masters.GetTimeZoneAsync(timeZoneId.Value, cancellationToken);

        return timeZone?.Name;
    }

    private async Task<OutcomeResponse> BuildOutcomeAsync(
        StateProvince state, string message, CancellationToken cancellationToken)
    {
        var cityCount = await masters.CountCitiesForStateAsync(state.Id, cancellationToken);

        return new OutcomeResponse(
            state.Id,
            state.Status.ToString(),
            state.Version,
            message,
            GlobalMasterMappingConfig.PermittedActionsFor(state, guard.IsSuperAdmin, cityCount));
    }
}
