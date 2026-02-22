namespace MediaHandler.Application.Common.Models;

public class Result
{
    public bool IsSuccess { get; }
    public string[] Errors { get; }

    protected Result(bool isSuccess, string[] errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public static Result Success() => new(true, Array.Empty<string>());
    public static Result Fail(params string[] errors) => new(false, errors);

    public static Result<T> Success<T>(T value) => new(true, value, Array.Empty<string>());
    public static Result<T> Fail<T>(params string[] errors) => new(false, default!, errors);
}

public class Result<T> : Result
{
    public T Value { get; }

    internal Result(bool isSuccess, T value, string[] errors) : base(isSuccess, errors)
    {
        Value = value;
    }
}
