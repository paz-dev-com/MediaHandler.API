namespace MediaHandler.API.Models;

public class ApiResponse<T>
{
    public T? Data { get; set; }
    public ApiResponseMeta? Meta { get; set; }
    public List<ApiError>? Errors { get; set; }

    public static ApiResponse<T> Success(T data, ApiResponseMeta? meta = null)
    {
        return new ApiResponse<T>
        {
            Data = data,
            Meta = meta,
            Errors = null
        };
    }

    public static ApiResponse<T> Fail(params ApiError[] errors)
    {
        return new ApiResponse<T>
        {
            Data = default,
            Meta = null,
            Errors = errors.ToList()
        };
    }
}

public class ApiResponse
{
    public object? Data { get; set; }
    public ApiResponseMeta? Meta { get; set; }
    public List<ApiError>? Errors { get; set; }

    public static ApiResponse Success(object? data = null, ApiResponseMeta? meta = null)
    {
        return new ApiResponse
        {
            Data = data,
            Meta = meta,
            Errors = null
        };
    }

    public static ApiResponse Fail(params ApiError[] errors)
    {
        return new ApiResponse
        {
            Data = null,
            Meta = null,
            Errors = errors.ToList()
        };
    }
}

public record ApiResponseMeta(
    int? Page = null,
    int? PageSize = null,
    int? TotalCount = null,
    int? TotalPages = null);

public record ApiError(
    string Code,
    string Message,
    string? Field = null);
