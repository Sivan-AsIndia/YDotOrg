namespace YDot.IAM.Application.Common.Abstractions.Services;

/// <summary>
/// Records the two request outcomes no handler is ever in a position to record.
///
/// WHY IT HAD TO EXIST. Every audit row in the platform was written by a handler, from inside the
/// same unit of work as the change it describes. That is the right design for an action that
/// happens - but it means the trail could only ever hold actions that got far enough to succeed,
/// and the audit screen's Outcome column showed nothing but "Succeeded" as the direct consequence.
/// Both of the other two outcomes the enum defines were unreachable in practice:
///
///   DENIED   an authorisation refusal is decided in the pipeline BEFORE any handler runs, so
///            there was no handler to write it and the 403 left no trace at all. The one thing an
///            audit trail is most often asked - "who tried to do what they were not allowed to" -
///            could not be answered.
///
///   FAILED   an unhandled error unwinds PAST the handler, so whatever the handler had tracked was
///            never saved. A dependency outage produced 500s and an empty trail.
///
/// IT WRITES IN ITS OWN SCOPE AND SAVES IMMEDIATELY. That is the load-bearing part: the request's
/// own DbContext is either untouched (a refusal, which happens before the handler) or holds
/// half-applied changes from a request that has just thrown (a failure). Saving through it would
/// be a no-op in the first case and would commit the wreckage in the second, so the row goes
/// through a fresh scope with a DbContext of its own.
///
/// A FAILURE HERE IS SWALLOWED. This runs while a request is already going wrong; an exception
/// escaping it would replace a 403 or a 500 the caller can act on with one nobody can.
/// </summary>
public interface IRequestOutcomeAuditor
{
    /// <summary>
    /// An authenticated caller was refused an endpoint.
    /// </summary>
    /// <param name="method">The HTTP method, for the metadata.</param>
    /// <param name="path">The route that was refused.</param>
    /// <param name="reason">
    /// What the pipeline refused on - the permission code where one is known, or the policy name.
    /// </param>
    Task RecordDeniedAsync(
        string method, string path, string? reason, CancellationToken cancellationToken = default);

    /// <summary>A request ended in an unhandled error.</summary>
    Task RecordFailedAsync(
        string method, string path, string? reason, CancellationToken cancellationToken = default);
}
