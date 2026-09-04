using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Domain.Entities.Configuration;

/// <summary>
/// One line of a payment gateway configuration's change log: who changed what, when, from what
/// to what.
///
/// WHY A TABLE OF ITS OWN WHEN <c>iam_audit_events</c> ALREADY EXISTS. The platform trail records
/// an action and a redacted metadata blob, which is right for a trail read across every module
/// and wrong for the panel on this screen: that panel is a per-field before-and-after, filtered
/// to one configuration, and building it by parsing JSON out of the general trail would be both
/// slow and fragile. BOTH ARE WRITTEN on every change - this table for the screen, the platform
/// trail for the auditor - in the same transaction as the change itself, so a configuration
/// cannot be altered without leaving both records.
///
/// ONE ROW PER CHANGED FIELD on an update, which is what makes the Old Value and New Value
/// columns mean anything. A create, a delete or a test writes a single summary row instead:
/// enumerating twelve fields against a null old value tells nobody anything.
///
/// NO SECRET IS EVER WRITTEN HERE, and that is not a matter of care at the call site - the
/// writer masks by field name, so a credential column records "set" or its four-character hint
/// and never its value. An audit trail that leaks the thing it was auditing is a liability
/// rather than a control.
/// </summary>
public sealed class PaymentGatewayConfigurationAudit : TenantEntity
{
    /// <summary>
    /// The configuration this row describes.
    ///
    /// NOT A CASCADING FOREIGN KEY. The log outlives the configuration: deleting a row whose
    /// credentials once took real money must leave behind the record of who deleted it.
    /// </summary>
    public Guid ConfigurationId { get; set; }

    public PaymentGatewayProvider Provider { get; set; }

    public PaymentGatewayEnvironment Environment { get; set; }

    public PaymentGatewayConfigurationAction Action { get; set; }

    /// <summary>The field that changed, or null on a summary row.</summary>
    public string? FieldName { get; set; }

    /// <summary>What it was. Masked for a credential, null when there was nothing before.</summary>
    public string? OldValue { get; set; }

    /// <summary>What it became. Masked for a credential.</summary>
    public string? NewValue { get; set; }

    /// <summary>Null for a system actor such as a seeding job.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Denormalised so the log still reads after the actor is removed.</summary>
    public string? ActorDisplayName { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Ties this row to the request and the log line that produced it.</summary>
    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Why, where the caller gave a reason.</summary>
    public string? Reason { get; set; }
}
