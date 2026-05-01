using MediaHandler.API.Models;
using MediaHandler.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace MediaHandler.API.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (statusCode, response) = exception switch
        {
            NotFoundException nfe => (StatusCodes.Status404NotFound,
                ApiResponse.Fail(new ApiError("NOT_FOUND", nfe.Message))),

            ValidationException ve => (StatusCodes.Status400BadRequest,
                ApiResponse.Fail(ve.Errors
                    .SelectMany(e => e.Value.Select(msg => new ApiError("VALIDATION_ERROR", msg, e.Key)))
                    .ToArray())),

            UnauthorizedAccessException => (StatusCodes.Status403Forbidden,
                ApiResponse.Fail(new ApiError("FORBIDDEN", "You do not have permission to perform this action."))),

            _ => (StatusCodes.Status500InternalServerError,
                ApiResponse.Fail(new ApiError("INTERNAL_ERROR", "An unexpected error occurred.")))
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(response, ct);
        return true;
    }
}