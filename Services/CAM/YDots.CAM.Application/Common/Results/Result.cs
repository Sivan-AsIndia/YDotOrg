namespace YDots.CAM.Application.Common.Results;

/// <summary>
/// Simple result pattern. Every handler returns a Result instead of throwing, so the
/// controller can turn one object into the correct HTTP status code.
///
/// THIS REPLACES THE OLD Result&lt;T&gt;, which carried an <c>HttpStatusCode</c> and a loose
/// error STRING. Two things were wrong with that. A free-text error cannot be branched on, so
/// the client had to match on message text or guess; and pairing the status code with the
/// message at every call site meant the same failure could answer 400 in one handler and 409
/// in another. The status now comes from the error catalogue, so one kind of failure has one
/// answer everywhere.
///
/// The brief asks for this shape and explicitly rules out ProblemDetails for now.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);
}
