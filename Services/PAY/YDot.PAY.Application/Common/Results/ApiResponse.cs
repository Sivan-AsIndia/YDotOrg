using System.Text.Json.Serialization;

namespace YDot.PAY.Application.Common.Results;

/// <summary>
/// The single response envelope used by every endpoint, for success and for failure. Byte-for-
/// byte the same shape IAM, DON and CAM return, so one Angular interceptor serves all four.
///
/// Every property carries <c>[JsonIgnore(Condition = Never)]</c> on purpose. The API is
/// configured with <c>DefaultIgnoreCondition = WhenWritingNull</c>, which would otherwise drop
/// the empty fields and give the caller a different set of keys on success and on failure.
/// </summary>
public sealed class ApiResponse<TData>
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TData? Data { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<ValidationError>? Errors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CorrelationId { get; init; }

    public static ApiResponse<TData> Ok(TData data, string? message = null, string? correlationId = null) =>
        new()
        {
            Success = true,
            Data = data,
            Message = message,
            ErrorCode = null,
            Errors = null,
            CorrelationId = correlationId
        };

    public static ApiResponse<TData> Fail(Error error, string? correlationId = null) =>
        new()
        {
            Success = false,
            Data = default,
            Message = error.Message,
            ErrorCode = error.Code,
            Errors = error.Errors,
            CorrelationId = correlationId
        };
}

/// <summary>Envelope for the writers that have no payload: filters, JWT events, middleware.</summary>
public sealed class ApiResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Data { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<ValidationError>? Errors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CorrelationId { get; init; }

    public static ApiResponse Ok(string? message = null, string? correlationId = null) =>
        new()
        {
            Success = true,
            Data = null,
            Message = message,
            ErrorCode = null,
            Errors = null,
            CorrelationId = correlationId
        };

    public static ApiResponse Fail(Error error, string? correlationId = null) =>
        new()
        {
            Success = false,
            Data = null,
            Message = error.Message,
            ErrorCode = error.Code,
            Errors = error.Errors,
            CorrelationId = correlationId
        };
}
