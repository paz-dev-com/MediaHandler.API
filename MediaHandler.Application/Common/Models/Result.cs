namespace MediaHandler.Application.Common.Models;

public class Result
{
    protected Result(bool isSuccess, string[] errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public string[] Errors { get; }

    public static Result Success()
    {
        return new Result(true, Array.Empty<string>());
    }

    public static Result Fail(params string[] errors)
    {
        return new Result(false, errors);
    }

    public static Result<T> Success<T>(T value)
    {
        return new Result<T>(true, value, Array.Empty<string>());
    }

    public static Result<T> Fail<T>(params string[] errors)
    {
        return new Result<T>(false, default!, errors);
    }
}

public class Result<T> : Result
{
    internal Result(bool isSuccess, T value, string[] errors) : base(isSuccess, errors)
    {
        Value = value;
    }

    public T Value { get; }
}