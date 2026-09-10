using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Writes the Denied and Failed rows the handlers cannot write. See
/// <see cref="IRequestOutcomeAuditor"/> for why they could not.
///
/// THE ROW IS BUILT FROM THE REQUEST'S OWN CONTEXT AND SAVED THROUGH A FRESH ONE, which is the
/// only detail here that is not obvious. <c>TenantContext</c> is request-scoped and filled in by
/// the tenant-resolution middleware, so a child scope would hand back an EMPTY one - the row
/// would be written against no Organisation and the Organisation whose trail it belongs in would
/// never see it. So the actor, the Organisation and the correlation id are read from the
/// injected, request-scoped services BEFORE the child scope is opened, and only the DbContext
/// comes from the new scope.
///
/// WHY A NEW DBCONTEXT AT ALL. On a refusal the request's own context is untouched, so saving
/// through it would work - but on a failure it holds whatever the handler had tracked when it
/// threw, and <c>SaveChangesAsync</c> there would commit a half-applied change as the side effect
/// of recording that it had gone wrong. One path for both is simpler than two, and the safe one
/// is the one that cannot commit anything else.
/// </summary>
public sealed class RequestOutcomeAuditor(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IServiceScopeFactory scopeFactory,
    ILogger<RequestOutcomeAuditor> logger) : IRequestOutcomeAuditor
{
    public Task RecordDeniedAsync(
        string method, string path, string? reason, CancellationToken cancellationToken = default) =>
        RecordAsync(AuditActionCodes.AccessDenied, AuditResult.Denied, method, path, reason, cancellationToken);

    public Task RecordFailedAsync(
        string method, string path, string? reason, CancellationToken cancellationToken = default) =>
        RecordAsync(AuditActionCodes.RequestFailed, AuditResult.Failed, method, path, reason, cancellationToken);

    private async Task RecordAsync(
        string actionCode,
        AuditResult result,
        string method,
        string path,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditEvent = new AuditEvent
            {
                BusinessUnitId = tenantContext.BusinessUnitId,
                TenantId = tenantContext.TenantId,
                ActorUserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
                ActorDisplayName = currentUser.DisplayName,
                ActorScope = tenantContext.Scope,
                ActionCode = actionCode,

                // THE ENDPOINT IS THE TARGET, because at this point in the pipeline nothing knows
                // which record was meant - the route has not been bound to a handler, or the
                // handler that would have named one has already thrown.
                TargetType = "Endpoint",
                TargetId = null,
                TargetDisplayName = Truncate($"{method} {path}", 300),
                Result = result,
                Reason = Truncate(reason, 1000),
                CorrelationId = currentUser.CorrelationId,
                OccurredAtUtc = clock.UtcNow,
                IpAddress = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
                ClientType = currentUser.ClientType,
                SessionId = currentUser.SessionId,

                // NOT FLAGGED SENSITIVE. A refusal and a failure are exactly what somebody with
                // plain audit access came to look at; hiding them behind the sensitive-detail
                // permission would put the trail's most-asked question out of reach of the people
                // who ask it. Nothing in the row is a credential - the path and the refused
                // permission code are both already known to the caller who was refused.
                IsSensitive = false,
                RequestPath = Truncate(path, 300)
            };

            using var scope = scopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<IamDbContext>();

            await context.AuditEvents.AddAsync(auditEvent, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // SWALLOWED ON PURPOSE. This runs while a request is already going wrong; letting an
            // exception out would replace a 403 or a 500 the caller can act on with one nobody
            // can, and would do it for the sake of a row that is only ever read later.
            logger.LogError(
                exception,
                "The {Result} outcome for {Method} {Path} could not be written to the audit trail.",
                result, method, path);
        }
    }

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength ? value : value[..maximumLength];
}
