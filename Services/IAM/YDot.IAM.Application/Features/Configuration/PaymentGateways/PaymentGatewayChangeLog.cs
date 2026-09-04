using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways;

/// <summary>
/// Builds the change-log rows for one save.
///
/// WHY IT IS A SEPARATE OBJECT RATHER THAN A METHOD ON THE HANDLER. Two things have to be true
/// of every row it produces and neither is enforceable if the rows are assembled inline:
/// credentials are masked by field name, and the actor, time, correlation id and IP come from
/// the ambient request rather than from whatever the caller thought to pass. Assembling them in
/// one place is what makes "the writer masks" a property of the code rather than a convention.
///
/// IT PRODUCES ROWS, IT DOES NOT SAVE THEM. The handler adds them in the same transaction as
/// the change they describe, so a configuration cannot be altered without its history being
/// written too - and a failed save takes both halves with it.
/// </summary>
public sealed class PaymentGatewayChangeLog(ICurrentUser currentUser, IDateTimeProvider clock)
{
    private readonly List<PaymentGatewayConfigurationAudit> _entries = [];

    public IReadOnlyList<PaymentGatewayConfigurationAudit> Entries => _entries;

    public bool HasEntries => _entries.Count > 0;

    /// <summary>
    /// Records one field that changed, if it actually changed.
    ///
    /// COMPARING BEFORE RECORDING IS THE WHOLE POINT of routing every field through here. A save
    /// that re-posts the form unchanged - which is what pressing Save twice does - would
    /// otherwise write fifteen rows saying nothing changed, and a change log nobody can skim is
    /// a change log nobody reads.
    /// </summary>
    public void Field(string fieldName, object? oldValue, object? newValue)
    {
        var before = Format(oldValue);
        var after = Format(newValue);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        Add(PaymentGatewayFields.IsMasked(fieldName)
                ? PaymentGatewayConfigurationAction.CredentialsRotated
                : PaymentGatewayConfigurationAction.Updated,
            fieldName,
            before,
            after);
    }

    /// <summary>
    /// Records a credential change without ever holding the credential.
    ///
    /// The caller passes the HINTS - the four-character tails - not the keys. What lands in the
    /// log is "set", "changed to ...abcd" or "cleared", which is what somebody reconstructing an
    /// incident actually needs: when the key changed, not what it changed to.
    /// </summary>
    public void Credential(string fieldName, bool hadOne, bool hasOne, string? newHint)
    {
        if (hadOne == hasOne && !hasOne)
        {
            return;
        }

        var before = hadOne ? "Set" : "Not set";

        var after = hasOne
            ? string.IsNullOrWhiteSpace(newHint) ? "Set" : $"Set ({newHint})"
            : "Cleared";

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        Add(PaymentGatewayConfigurationAction.CredentialsRotated, fieldName, before, after);
    }

    /// <summary>A single row for something that is not a field change: created, deleted, tested.</summary>
    public void Summary(PaymentGatewayConfigurationAction action, string? newValue = null) =>
        Add(action, fieldName: null, oldValue: null, newValue);

    /// <summary>Stamps every row built so far with the configuration it belongs to.</summary>
    public IReadOnlyList<PaymentGatewayConfigurationAudit> For(
        PaymentGatewayConfiguration configuration, string? reason)
    {
        foreach (var entry in _entries)
        {
            entry.ConfigurationId = configuration.Id;
            entry.TenantId = configuration.TenantId;
            entry.BusinessUnitId = configuration.BusinessUnitId;
            entry.Provider = configuration.Provider;
            entry.Environment = configuration.Environment;
            entry.Reason = reason;
        }

        return _entries;
    }

    private void Add(
        PaymentGatewayConfigurationAction action, string? fieldName, string? oldValue, string? newValue) =>
        _entries.Add(new PaymentGatewayConfigurationAudit
        {
            Action = action,
            FieldName = fieldName,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),

            // From the ambient request, never from the caller. There is no field a busy handler
            // can forget, which is the same reasoning IAuditService is built on.
            ActorUserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
            ActorDisplayName = currentUser.DisplayName ?? currentUser.Username,
            OccurredAtUtc = clock.UtcNow,
            CorrelationId = currentUser.CorrelationId,
            IpAddress = currentUser.IpAddress
        });

    /// <summary>
    /// One rendering of a value, so a comparison is between two strings and not between an int
    /// and a string that happen to look alike.
    /// </summary>
    private static string? Format(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "Active" : "Inactive",
        IEnumerable<string> items => string.Join(", ", items.OrderBy(item => item, StringComparer.Ordinal)),
        Enum member => member.ToString(),
        _ => value.ToString()
    };

    /// <summary>
    /// Keeps a row inside its column.
    ///
    /// A long note or a list of methods would otherwise fail the insert, and losing the whole
    /// change record because somebody wrote a paragraph in Notes would be the wrong trade.
    /// </summary>
    private static string? Truncate(string? value) =>
        value is { Length: > 1000 } ? value[..997] + "..." : value;
}
