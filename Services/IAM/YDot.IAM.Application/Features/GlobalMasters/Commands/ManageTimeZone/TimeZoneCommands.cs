using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Application.Features.GlobalMasters.Mappings;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.Commands.ManageTimeZone;

/// <summary>Creates a time zone.</summary>
public sealed record CreateTimeZoneCommand(CreateTimeZoneRequest Request);

/// <summary>Edits a time zone.</summary>
public sealed record UpdateTimeZoneCommand(Guid TimeZoneId, UpdateTimeZoneRequest Request);

/// <summary>Activates or deactivates a time zone.</summary>
public sealed record ChangeTimeZoneStatusCommand(Guid TimeZoneId, ChangeMasterStatusRequest Request);

/// <summary>Deletes a time zone. Refused while any state defaults to it.</summary>
public sealed record DeleteTimeZoneCommand(Guid TimeZoneId, DeleteMasterRequest Request);

/// <summary>
/// Time-zone maintenance.
///
/// THE OFFSET IS VALIDATED AS A RANGE, NOT AS A WHOLE NUMBER OF HOURS. Real zones run from
/// -12:00 to +14:00 and several of them sit on a 30- or 45-minute boundary - India is +330,
/// Nepal +345, the Chatham Islands +765. A column in hours, or a check that rejected anything
/// not divisible by 60, would silently make those zones unrepresentable.
///
/// THE CODE IS DERIVED FROM THE IANA KEY rather than authored separately, so the two cannot
/// disagree. See <c>TimeZoneMappingConfig.ToCode</c> for why that mapping is not the usual
/// <c>CodeValue.FromName</c>.
/// </summary>
public sealed class TimeZoneCommandHandler(
    IGlobalMasterRepository masters,
    IAuditService audit,
    GlobalMasterWriteGuard guard,
    IUnitOfWork unitOfWork)
{
    /// <summary>The real-world extremes of UTC offset, in minutes.</summary>
    private const int MinimumOffsetMinutes = -12 * 60;

    private const int MaximumOffsetMinutes = 14 * 60;

    public async Task<Result<TimeZoneDetailResponse>> HandleAsync(
        CreateTimeZoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var scopeTenantId = guard.WriteScopeTenantId;

        var offsetCheck = ValidateOffset(request.StandardUtcOffsetMinutes);
        if (offsetCheck.IsFailure)
        {
            return Result.Failure<TimeZoneDetailResponse>(offsetCheck.Error!);
        }

        var ianaKey = request.TimeZoneKey?.Trim();
        if (string.IsNullOrWhiteSpace(ianaKey))
        {
            return Result.Failure<TimeZoneDetailResponse>(Error.Validation(
                "A time zone needs an IANA key.",
                [new ValidationError(
                    nameof(request.TimeZoneKey),
                    "Use the IANA identifier, such as Asia/Kolkata.")]));
        }

        if (await masters.IanaKeyExistsAsync(ianaKey, scopeTenantId, null, cancellationToken))
        {
            return Result.Failure<TimeZoneDetailResponse>(
                Error.Duplicate($"The time zone {ianaKey} already exists in this catalogue."));
        }

        var timeZone = request.ToEntity(scopeTenantId, guard.BusinessUnitId);

        await masters.AddAsync(timeZone, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterCreated,
            nameof(TimeZoneDefinition),
            timeZone.Id,
            timeZone.Name,
            new
            {
                timeZone.IanaKey,
                timeZone.OffsetDisplay,
                Scope = scopeTenantId is null ? "Platform" : "Organisation"
            },
            cancellationToken: cancellationToken);

        return timeZone.ToDetailResponse(stateUsageCount: 0, isSuperAdmin: guard.IsSuperAdmin);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        UpdateTimeZoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var timeZone = await masters.GetTimeZoneAsync(command.TimeZoneId, cancellationToken);
        if (timeZone is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That time zone was not found."));
        }

        var guarded = GuardWrite(timeZone, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        if (request.StandardUtcOffsetMinutes.HasValue)
        {
            var offsetCheck = ValidateOffset(request.StandardUtcOffsetMinutes.Value);
            if (offsetCheck.IsFailure)
            {
                return Result.Failure<OutcomeResponse>(offsetCheck.Error!);
            }
        }

        var previousOffset = timeZone.OffsetDisplay;

        request.ApplyTo(timeZone);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterUpdated,
            nameof(TimeZoneDefinition),
            timeZone.Id,
            timeZone.Name,
            new
            {
                timeZone.IanaKey,
                PreviousOffset = previousOffset,
                NewOffset = timeZone.OffsetDisplay,
                OffsetChanged = !string.Equals(previousOffset, timeZone.OffsetDisplay, StringComparison.Ordinal)
            },
            cancellationToken: cancellationToken);

        return await BuildOutcomeAsync(timeZone, "Time zone updated.", cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        ChangeTimeZoneStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var timeZone = await masters.GetTimeZoneAsync(command.TimeZoneId, cancellationToken);
        if (timeZone is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That time zone was not found."));
        }

        var guarded = GuardWrite(timeZone, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        if (timeZone.Status == request.Status)
        {
            return Result.Failure<OutcomeResponse>(
                Error.InvalidTransition($"That time zone is already {request.Status}."));
        }

        timeZone.Status = request.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            request.Status == MasterDataStatus.Active
                ? AuditActionCodes.GlobalMasterActivated
                : AuditActionCodes.GlobalMasterDeactivated,
            nameof(TimeZoneDefinition),
            timeZone.Id,
            timeZone.Name,
            new { timeZone.IanaKey, NewStatus = request.Status.ToString() },
            request.Reason,
            cancellationToken);

        return await BuildOutcomeAsync(
            timeZone,
            request.Status == MasterDataStatus.Active ? "Time zone activated." : "Time zone deactivated.",
            cancellationToken);
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteTimeZoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var timeZone = await masters.GetTimeZoneAsync(command.TimeZoneId, cancellationToken);
        if (timeZone is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That time zone was not found."));
        }

        var guarded = GuardWrite(timeZone, request.ExpectedVersion);
        if (guarded.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(guarded.Error!);
        }

        var usageCount = await masters.CountStatesUsingTimeZoneAsync(timeZone.Id, cancellationToken);

        var free = GlobalMasterWriteGuard.EnsureNoDependents(
            usageCount, $"The time zone {timeZone.IanaKey}", "states defaulting to it");

        if (free.IsFailure)
        {
            return Result.Failure<OutcomeResponse>(free.Error!);
        }

        var snapshot = new { timeZone.IanaKey, timeZone.Name, timeZone.StandardUtcOffsetMinutes };

        masters.Remove(timeZone);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.GlobalMasterDeleted,
            nameof(TimeZoneDefinition),
            timeZone.Id,
            timeZone.Name,
            snapshot,
            request.Reason,
            cancellationToken);

        return new OutcomeResponse(
            timeZone.Id, timeZone.Status.ToString(), timeZone.Version, "Time zone deleted.", []);
    }

    private Result GuardWrite(TimeZoneDefinition timeZone, long expectedVersion)
    {
        var writable = guard.EnsureWritable(timeZone, $"The time zone {timeZone.IanaKey}");

        return writable.IsFailure
            ? writable
            : GlobalMasterWriteGuard.EnsureVersionMatches(timeZone, expectedVersion);
    }

    /// <summary>Rejects an offset outside the range real time zones occupy.</summary>
    private static Result ValidateOffset(int offsetMinutes) =>
        offsetMinutes is >= MinimumOffsetMinutes and <= MaximumOffsetMinutes
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "That UTC offset is out of range.",
                [new ValidationError(
                    "standardUtcOffsetMinutes",
                    "Offsets run from -720 minutes (-12:00) to +840 minutes (+14:00).")]));

    private async Task<OutcomeResponse> BuildOutcomeAsync(
        TimeZoneDefinition timeZone, string message, CancellationToken cancellationToken)
    {
        var usageCount = await masters.CountStatesUsingTimeZoneAsync(timeZone.Id, cancellationToken);

        return new OutcomeResponse(
            timeZone.Id,
            timeZone.Status.ToString(),
            timeZone.Version,
            message,
            GlobalMasterMappingConfig.PermittedActionsFor(timeZone, guard.IsSuperAdmin, usageCount));
    }
}
