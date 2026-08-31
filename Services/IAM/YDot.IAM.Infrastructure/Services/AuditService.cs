using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Writes the append-only audit trail.
///
/// THE ROW IS ASSEMBLED HERE, NOT BY THE CALLER. Actor, Organisation, scope, IP, user agent,
/// client type, session and correlation id all come from the ambient request context, so a
/// handler supplies only what is specific to the action it just performed. That is what keeps
/// the trail complete: there is no field a busy handler can forget to populate.
///
/// EVERYTHING IS REDACTED ON THE WAY IN. <see cref="Redact"/> strips anything whose property
/// name looks like a secret before the metadata is serialised. An audit trail that leaks the
/// credential it was auditing is a liability rather than a control, and the safest place to
/// enforce that is the one door every row goes through.
/// </summary>
public sealed partial class AuditService(
    IAuditRepository repository,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public Task WriteAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        string? targetDisplayName = null,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(actionCode, targetType, targetId, AuditResult.Succeeded,
            targetDisplayName, metadata, reason, cancellationToken);

    public async Task WriteAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        AuditResult result,
        string? targetDisplayName = null,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            BusinessUnitId = tenantContext.BusinessUnitId,
            TenantId = tenantContext.TenantId,
            ActorUserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
            ActorDisplayName = currentUser.DisplayName,

            // Global when a root user did this, Tenant otherwise. Paired with TenantId it is
            // what lets an Organisation see "somebody from the platform did this to my data"
            // without the row pretending the actor was one of their own users.
            ActorScope = tenantContext.Scope,

            ActionCode = actionCode,
            TargetType = targetType,
            TargetId = targetId,
            TargetDisplayName = Truncate(targetDisplayName, 300),
            Result = result,
            Reason = Truncate(reason, 1000),
            CorrelationId = currentUser.CorrelationId,
            OccurredAtUtc = clock.UtcNow,
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            ClientType = currentUser.ClientType,
            SessionId = currentUser.SessionId,
            Metadata = Serialise(metadata),
            IsSensitive = IsSensitiveAction(actionCode),
            RequestPath = Truncate(httpContextAccessor.HttpContext?.Request.Path.Value, 300)
        };

        await repository.AddAsync(auditEvent, cancellationToken);
    }

    /// <summary>
    /// An action with no authenticated caller: a failed sign-in, an invitation accepted by
    /// somebody who is not yet a user. The Organisation is passed explicitly, because there is
    /// no context to read it from.
    /// </summary>
    public async Task WriteAnonymousAsync(
        string actionCode,
        string targetType,
        Guid? targetId,
        Guid businessUnitId,
        Guid? tenantId,
        AuditResult result,
        string? targetDisplayName = null,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            BusinessUnitId = businessUnitId == Guid.Empty ? tenantContext.BusinessUnitId : businessUnitId,
            TenantId = tenantId,
            ActorUserId = null,
            ActorDisplayName = null,
            ActorScope = AccessScopeType.Tenant,
            ActionCode = actionCode,
            TargetType = targetType,
            TargetId = targetId,
            TargetDisplayName = Truncate(targetDisplayName, 300),
            Result = result,
            CorrelationId = currentUser.CorrelationId,
            OccurredAtUtc = clock.UtcNow,
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            ClientType = currentUser.ClientType,
            Metadata = Serialise(metadata),
            IsSensitive = IsSensitiveAction(actionCode),
            RequestPath = Truncate(httpContextAccessor.HttpContext?.Request.Path.Value, 300)
        };

        await repository.AddAsync(auditEvent, cancellationToken);
    }

    /// <summary>
    /// A cross-Tenant access attempt.
    ///
    /// The query filters make this close to unreachable, so a row written here means somebody
    /// got past several layers deliberately. It is logged at WARNING as well as recorded,
    /// because unlike every other audit row this one should wake somebody up rather than sit
    /// in a table waiting to be queried.
    /// </summary>
    public async Task WriteCrossTenantAttemptAsync(
        string targetType,
        Guid? targetId,
        Guid attemptedTenantId,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Cross-tenant access attempt. Actor {ActorId} operating in {CurrentTenantId} "
            + "tried to reach {TargetType} {TargetId} in {AttemptedTenantId}. Correlation {CorrelationId}.",
            currentUser.UserId, tenantContext.TenantId, targetType, targetId,
            attemptedTenantId, currentUser.CorrelationId);

        await WriteAsync(
            AuditActionCodes.CrossTenantAccessAttempt,
            targetType,
            targetId,
            AuditResult.Denied,
            targetDisplayName: null,
            new { AttemptedTenantId = attemptedTenantId, CurrentTenantId = tenantContext.TenantId },
            reason: "Attempted to access data belonging to a different organisation.",
            cancellationToken);
    }

    /// <summary>
    /// Serialises the metadata, redacting anything that looks like a secret first.
    ///
    /// A serialisation failure must never break the operation being audited, so it is caught
    /// and recorded as a note rather than thrown.
    /// </summary>
    private string? Serialise(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(metadata, SerializerOptions);
            var redacted = Redact(json);

            return Truncate(redacted, 8000);
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(exception, "Audit metadata could not be serialised and was dropped.");
            return "{\"note\":\"metadata could not be serialised\"}";
        }
    }

    /// <summary>
    /// Replaces the VALUE of any property whose name looks like a credential.
    ///
    /// Deliberately blunt and deliberately applied to every row. The alternative — trusting
    /// each of a hundred call sites never to pass a secret — is exactly the kind of discipline
    /// that holds for a year and then does not.
    /// </summary>
    private static string Redact(string json) => SecretPattern().Replace(json, "$1\"***redacted***\"");

    private static bool IsSensitiveAction(string actionCode) =>
        actionCode.Contains("password", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("mfa", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("token", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("permission", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("role", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("approve", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("reject", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("export", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("suspend", StringComparison.OrdinalIgnoreCase)
        || actionCode.Contains("cross-tenant", StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength ? value : value[..maximumLength];

    /// <summary>
    /// Matches a JSON property whose name suggests a secret, capturing the name so the value
    /// alone is replaced and the shape of the object survives for readability.
    /// </summary>
    [GeneratedRegex(
        "(\"(?:[a-zA-Z]*(?:password|secret|token|hash|credential|apikey|privatekey|salt|otp|code)[a-zA-Z]*)\"\\s*:\\s*)\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex SecretPattern();
}
