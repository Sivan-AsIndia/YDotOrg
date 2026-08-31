using System.Runtime.CompilerServices;

// TenantContext.Set and TenantContext.SwitchTenant are internal ON PURPOSE: the Organisation a
// request operates in must be settable by exactly one thing, the tenant-resolution middleware,
// and by nothing in the application layer.
//
// That middleware necessarily lives in the API project, because it is pipeline wiring. This
// attribute is what lets it reach the setter while keeping every handler, repository and
// service locked out — the alternative would be making the setter public, at which point any
// code anywhere could re-point the request at a different Organisation.
//
// Nothing else is granted, and nothing else should be added here without the same kind of
// reason.
[assembly: InternalsVisibleTo("YDot.IAM.Api")]
