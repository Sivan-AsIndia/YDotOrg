namespace YDot.PAY.Application.Common.Abstractions.Security;

/// <summary>
/// Which Organisation the current request is operating in.
///
/// IT HAS TWO SOURCES IN THIS SERVICE, unlike anywhere else, and that is the interesting part.
/// A staff request resolves the Organisation from the validated token. A PUBLIC DONATION request
/// has no token at all - it arrives with an intent reference or a tracking reference, and the
/// Organisation is resolved from the row those identify.
///
/// The second path is why <see cref="SetFromPublicContext"/> exists on the implementation. It is
/// the one place an Organisation is derived from something the caller sent, which is safe only
/// because an intent reference is unguessable and resolves to exactly one row: the caller is not
/// CHOOSING an Organisation, they are naming a record that already belongs to one.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    Guid BusinessUnitId { get; }

    string? TenantCode { get; }

    string? TenantName { get; }

    bool HasTenant { get; }

    bool IsSuperAdmin { get; }

    /// <summary>True when the Organisation came from a public donation reference, not a token.</summary>
    bool IsPublicDonorContext { get; }

    /// <summary>
    /// The Organisation, or an exception if there is none.
    ///
    /// A MONEY-OWNING WRITE MUST NOT PROCEED WITHOUT ONE. Returning Guid.Empty would write a
    /// donation owned by nobody, which no query filter would ever return again - income that
    /// silently vanishes.
    /// </summary>
    Guid RequireTenantId();
}
