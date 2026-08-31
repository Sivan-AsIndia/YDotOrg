namespace YDot.PAY.Application.Common.Results;

/// <summary>
/// Simple result pattern. Every handler returns a Result instead of throwing, so the controller
/// can turn one object into the correct HTTP status code.
///
/// IT MATTERS MORE IN THIS SERVICE THAN ANYWHERE ELSE. A payment handler has a dozen legitimate
/// non-success outcomes - declined, expired, already paid, sign in first, still confirming - and
/// every one of them is an ordinary answer rather than an exceptional condition. Modelling them
/// as exceptions would make the normal path the one that throws.
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
