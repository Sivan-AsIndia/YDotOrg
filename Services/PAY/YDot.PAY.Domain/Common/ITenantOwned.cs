namespace YDot.PAY.Domain.Common;

/// <summary>
/// THE ISOLATION MARKER. Every entity that belongs to exactly one Organisation implements this,
/// and it is the only thing <c>PaymentDbContext</c> looks at when it decides whether to attach
/// a global query filter.
///
/// IT MATTERS MORE HERE THAN ANYWHERE ELSE IN THE PLATFORM. These tables hold money: donation
/// amounts, gateway references, receipt numbers and refund decisions. A read that crossed an
/// Organisation boundary would not merely leak a name, it would show one charity another
/// charity's income.
///
/// Implementing it has three automatic consequences, none of which a handler has to remember:
/// a global query filter on every read, the owner stamped from the request context on insert,
/// and TenantId included in every unique constraint - so two Organisations can each issue
/// receipt number 0001 while neither can issue it twice.
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning Organisation. Displayed as "Organisation" in the UI.</summary>
    Guid TenantId { get; set; }

    /// <summary>The root boundary above Tenant, denormalised for BusinessUnit-wide reporting.</summary>
    Guid BusinessUnitId { get; set; }
}
