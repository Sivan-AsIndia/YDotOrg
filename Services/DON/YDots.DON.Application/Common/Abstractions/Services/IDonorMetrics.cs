namespace YDots.DON.Application.Common.Abstractions.Services;

/// <summary>
/// The four observability signals section 10 asks for: ydot_don_request_duration, a transition
/// count, a failure count broken down by stable error code, and a dependency trace.
///
/// Kept behind an interface so the application layer can record a transition without taking a
/// dependency on a metrics library.
/// </summary>
public interface IDonorMetrics
{
    /// <summary>ydot_don_request_duration, in milliseconds, tagged by route and status code.</summary>
    void RecordRequestDuration(string route, string method, int statusCode, double elapsedMilliseconds);

    /// <summary>
    /// ydot_don_transition_count. One increment per lifecycle move, tagged with the action code
    /// and the outcome, so "how many donors were approved this week" is a query rather than a
    /// table scan.
    /// </summary>
    void RecordTransition(string actionCode, string targetType, string result);

    /// <summary>ydot_don_failure_count, tagged with the stable error code from section 11.</summary>
    void RecordFailure(string errorCode, int statusCode, string route);
}
