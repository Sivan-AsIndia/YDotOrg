using System.Text.Json.Serialization;

namespace YDot.IAM.Application.Common.Results;

/// <summary>
/// The single response envelope used by every endpoint, for success and for failure. It is
/// byte-for-byte the same shape DON returns, so the Angular interceptor written for one
/// service works for the other without a second code path.
///
/// Every property carries <c>[JsonIgnore(Condition = Never)]</c> on purpose. The API is
/// configured with <c>DefaultIgnoreCondition = WhenWritingNull</c>, which would otherwise
/// drop the empty fields and give the caller a different set of keys on success and on
/// failure. The attribute overrides that for the envelope only: all six keys are always
/// written. Payload DTOs inside <c>Data</c> keep the global behaviour and still omit their
/// own nulls.
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

/// <summary>
/// Envelope for the writers that have no payload to return: the validation filter, the model
/// binding factory, the JWT challenge events and the exception middleware. <c>Data</c> is
/// declared here as well, always null, so a failure produced outside a controller has
/// exactly the same six keys as one produced inside it.
/// </summary>
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
