using System.Diagnostics;
using System.Diagnostics.Metrics;
using YDots.DON.Application.Common.Abstractions.Services;

namespace YDots.DON.Infrastructure.Observability;

/// <summary>
/// The section 10 observability signals, built on System.Diagnostics.Metrics from the base
/// class library. No exporter is configured here on purpose: a Meter with no listener costs
/// almost nothing, and whichever backend the platform later chooses — OpenTelemetry,
/// Prometheus, Application Insights — subscribes to these same instrument names without any
/// change to this class.
/// </summary>
public sealed class DonorMetrics : IDonorMetrics, IDisposable
{
    /// <summary>The meter name a collector subscribes to.</summary>
    public const string MeterName = "YDots.DON";

    /// <summary>The activity source name for the dependency trace.</summary>
    public const string ActivitySourceName = "YDots.DON";

    /// <summary>Spans started here carry the correlation id, so a trace and a log line line up.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Meter _meter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _transitionCount;
    private readonly Counter<long> _failureCount;

    public DonorMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _requestDuration = _meter.CreateHistogram<double>(
            "ydot_don_request_duration",
            unit: "ms",
            description: "How long a Donors API request took, by route and status code.");

        _transitionCount = _meter.CreateCounter<long>(
            "ydot_don_transition_count",
            unit: "transitions",
            description: "Lifecycle transitions recorded by the Donors section.");

        _failureCount = _meter.CreateCounter<long>(
            "ydot_don_failure_count",
            unit: "failures",
            description: "Failed Donors API requests, by stable error code.");
    }

    public void RecordRequestDuration(string route, string method, int statusCode, double elapsedMilliseconds) =>
        _requestDuration.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("status_code", statusCode));

    public void RecordTransition(string actionCode, string targetType, string result) =>
        _transitionCount.Add(
            1,
            new KeyValuePair<string, object?>("action_code", actionCode),
            new KeyValuePair<string, object?>("target_type", targetType),
            new KeyValuePair<string, object?>("result", result));

    public void RecordFailure(string errorCode, int statusCode, string route) =>
        _failureCount.Add(
            1,
            new KeyValuePair<string, object?>("error_code", errorCode),
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("route", route));

    public void Dispose() => _meter.Dispose();
}
