namespace YDots.DON.Application.Common.Abstractions.Security;

/// <summary>
/// Which Organisation the current request is operating in.
///
/// SEPARATE FROM <see cref="ICurrentUser"/> ON PURPOSE, even though both read the same token.
/// The two answer different questions, and the DbContext needs the second one WITHOUT taking a
/// dependency on the first: a query filter that resolved ICurrentUser would drag
/// IHttpContextAccessor, the permission set and the data scopes into the persistence layer to
/// answer "which Organisation?".
///
/// EVERYTHING TRUSTS THIS OBJECT COMPLETELY - the query filters, the write stamping - so it is
/// resolved once, from the validated token, by middleware that runs before any handler.
/// </summary>
public interface ITenantContext
{
    /// <summary>The resolved Organisation, or null when the request has none.</summary>
    Guid? OrganisationId { get; }

    /// <summary>True once an Organisation has been resolved for this request.</summary>
    bool HasOrganisation { get; }

    /// <summary>
    /// The Organisation, or an exception if there is none.
    ///
    /// AN ORGANISATION-OWNED WRITE MUST NOT PROCEED WITHOUT ONE. Returning Guid.Empty instead
    /// would write a row owned by nobody, which no query filter would ever return again - a
    /// silent data loss that looks like a successful save.
    /// </summary>
    Guid RequireOrganisationId();
}
