namespace YDot.IAM.Application.Common.Results;

/// <summary>
/// Simple result pattern. Every handler returns a Result instead of throwing, so the
/// controller can turn one object into the correct HTTP status code.
///
/// The brief asks for this and explicitly rules out ProblemDetails for now, so the shape
/// here is the whole error contract: a stable code, a human message, and optionally a list
/// of field errors.
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
