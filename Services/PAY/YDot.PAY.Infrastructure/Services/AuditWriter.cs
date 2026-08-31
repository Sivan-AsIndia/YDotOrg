using System.Text.Json;
using System.Text.Json.Nodes;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Writes the append-only payment audit trail.
///
/// THE ROW IS ASSEMBLED HERE, NOT BY THE CALLER. Actor, Organisation, business unit, IP address,
/// correlation id and timestamp all come from the ambient request context, so a handler supplies
/// only what is specific to the action. That is what keeps the trail complete: there is no field
/// a busy handler can forget.
///
/// IT ADDS TO THE CHANGE TRACKER AND DOES NOT SAVE. The audit row commits in the same
/// transaction as the change it records, which is the only way the two can never disagree - an
/// audit row saved separately can survive a rolled-back change, and a change can commit without
/// its audit row. On a money service that second failure is the one that matters: a refund that
/// happened with no record of who approved it.
///
/// THE METADATA IS SCRUBBED ON THE WAY IN. See <see cref="Scrub"/>.
/// </summary>
public sealed class AuditWriter(
    PaymentDbContext context,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IAuditWriter
{
    private const int ReasonMaximumLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Property names whose values never reach the audit table.
    ///
    /// AN AUDIT TRAIL THAT LEAKS THE THING IT WAS AUDITING IS A LIABILITY RATHER THAN A CONTROL.
    /// A gateway response object passed as metadata can carry a card number, a CVV, an API key or
    /// a webhook secret; the row is then a permanent, queryable copy of exactly what the rest of
    /// the module works hard never to store.
    ///
    /// Matched case-insensitively on a CONTAINS, not an equals, because the same value arrives
    /// under a dozen names - <c>cardNumber</c>, <c>card_number</c>, <c>pan</c>,
    /// <c>customerCardNumber</c> - and a whitelist of exact names is a list somebody's next
    /// integration falls outside of.
    /// </summary>
    private static readonly string[] SensitiveKeyFragments =
    [
        "cardnumber", "card_number", "cardno", "pan",
        "cvv", "cvc", "securitycode",
        "expiry", "expirydate", "exp_month", "exp_year",
        "password", "secret", "apikey", "api_key", "accesstoken", "access_token",
        "authorization", "signature", "privatekey", "private_key",
        "accountnumber", "account_number", "iban", "sortcode", "routingnumber"
    ];

    public Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            actionCode, targetType, targetId, AuditResult.Succeeded, metadata, reason, cancellationToken);

    public async Task WriteAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        AuditResult result,
        object? metadata = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(
            new PaymentAuditEvent
            {
                TenantId = tenantContext.TenantId,
                BusinessUnitId = tenantContext.BusinessUnitId == Guid.Empty
                    ? null
                    : tenantContext.BusinessUnitId,

                // Null rather than Guid.Empty for an unauthenticated actor, so "nobody was signed
                // in" is distinguishable from "the actor was not recorded". In this service the
                // first is routine - a public donation - and the second would be a bug.
                ActorUserId = currentUser.UserId == Guid.Empty ? null : currentUser.UserId,

                ActionCode = actionCode,
                TargetType = targetType,
                TargetId = targetId,
                Result = result,
                Reason = Truncate(reason),
                Metadata = Serialise(metadata),
                CorrelationId = currentUser.CorrelationId,
                IpAddress = currentUser.IpAddress,
                OccurredAtUtc = clock.UtcNow
            },
            cancellationToken);
    }

    /// <summary>
    /// An action with no authenticated caller: a public donation, a gateway webhook.
    ///
    /// THE ORGANISATION IS PASSED EXPLICITLY because there is no token to read it from, and these
    /// are precisely the rows an investigation looks at first - a webhook whose signature failed,
    /// a donation initiated from an unexpected address. Recording them as belonging to nobody
    /// would make the one trail that matters unsearchable.
    ///
    /// It may legitimately be called with a null Organisation, when a webhook could not be
    /// resolved at all. That row is still worth having: "something posted a callback we could
    /// not attribute" is exactly what somebody needs to see.
    /// </summary>
    public async Task WriteAnonymousAsync(
        string actionCode,
        string targetType,
        Guid targetId,
        Guid? tenantId,
        AuditResult result,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await context.AuditEvents.AddAsync(
            new PaymentAuditEvent
            {
                TenantId = tenantId,
                BusinessUnitId = null,
                ActorUserId = null,
                ActionCode = actionCode,
                TargetType = targetType,
                TargetId = targetId,
                Result = result,
                Reason = null,
                Metadata = Serialise(metadata),
                CorrelationId = currentUser.CorrelationId,
                IpAddress = currentUser.IpAddress,
                OccurredAtUtc = clock.UtcNow
            },
            cancellationToken);
    }

    /// <summary>
    /// Serialises the metadata, then scrubs it.
    ///
    /// IT NEVER THROWS. An audit row with degraded metadata is worth having; an exception here
    /// would fail the operation being audited, which turns a logging problem into a refused
    /// donation.
    /// </summary>
    private static string? Serialise(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            var node = JsonSerializer.SerializeToNode(metadata, SerializerOptions);

            if (node is null)
            {
                return null;
            }

            Scrub(node);

            return node.ToJsonString(SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return JsonSerializer.Serialize(
                new { serialisationFailed = true, type = metadata.GetType().Name }, SerializerOptions);
        }
    }

    /// <summary>
    /// Replaces the value of any sensitive-looking property, at any depth.
    ///
    /// THE VALUE IS REPLACED, NOT REMOVED. "[redacted]" tells a reader that the field was present
    /// and deliberately withheld; a missing key looks like the integration never sent it, which
    /// is a different and misleading fact.
    /// </summary>
    private static void Scrub(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (IsSensitive(property.Key))
                    {
                        jsonObject[property.Key] = "[redacted]";
                        continue;
                    }

                    if (property.Value is not null)
                    {
                        Scrub(property.Value);
                    }
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null)
                    {
                        Scrub(item);
                    }
                }

                break;
        }
    }

    private static bool IsSensitive(string propertyName)
    {
        var normalised = propertyName.Replace("-", string.Empty, StringComparison.Ordinal);

        return SensitiveKeyFragments.Any(fragment =>
            normalised.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= ReasonMaximumLength ? value : value[..ReasonMaximumLength];
    }
}
