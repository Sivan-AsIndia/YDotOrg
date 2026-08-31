namespace YDots.CAM.Application.Common.Results;

/// <summary>Result that carries a value when the operation succeeds.</summary>
public sealed class Result<TValue> : Result
{
    private Result(bool isSuccess, TValue? value, Error? error)
        : base(isSuccess, error) => Value = value;

    public TValue? Value { get; }

    public static Result<TValue> Success(TValue value) => new(true, value, null);

    public static new Result<TValue> Failure(Error error) => new(false, default, error);

    /// <summary>Lets a handler write "return response;" instead of "return Result.Success(response);".</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
