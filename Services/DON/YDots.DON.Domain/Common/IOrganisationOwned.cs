namespace YDots.DON.Domain.Common;

/// <summary>
/// THE ISOLATION MARKER. Every entity that belongs to exactly one Organisation implements
/// this, and it is the only thing <c>DonDbContext</c> looks at when it decides whether to
/// attach a global query filter.
///
/// WHY THIS EXISTS WHEN THE COLUMN ALREADY DID. <c>OrganisationId</c> has been on these
/// entities from the start, but it was only ever a column: isolation depended on every single
/// repository method remembering <c>Where(x =&gt; x.OrganisationId == scope.OrganisationId)</c>,
/// and on every insert remembering to set it. One forgotten Where clause is one Organisation
/// reading another's donors, and nothing in the type system would have said so - the code
/// compiles, the tests pass, and the leak is invisible.
///
/// Declaring the interface over the EXISTING column turns that convention into a property of
/// the model, with no schema change at all. Implementing it has three automatic consequences:
///
///   1. <c>DonDbContext</c> attaches a global query filter, so a read can never see another
///      Organisation's row even where a Where clause was forgotten.
///   2. <c>SaveChangesAsync</c> stamps OrganisationId from the request context on insert, so a
///      caller cannot choose it, and refuses to move an existing row between Organisations.
///   3. A new entity is isolated the moment it declares the interface, with nothing to
///      remember.
///
/// THE EXPLICIT WHERE CLAUSES IN THE REPOSITORIES STAY. They are not redundant: a filter is
/// defence in depth, and <c>IgnoreQueryFilters()</c> walks straight past it. Belt and braces is
/// the right posture for the boundary between two Organisations' donor records.
///
/// DO NOT implement it on <c>DonorContact</c> or <c>DonorTag</c>. Those are child rows reached
/// only through their Donor, which is itself filtered - so they are already unreachable across
/// an Organisation boundary, and giving them a filter of their own would need a column they do
/// not have.
/// </summary>
public interface IOrganisationOwned
{
    /// <summary>The owning Organisation. The isolation boundary for the whole module.</summary>
    Guid OrganisationId { get; set; }
}
