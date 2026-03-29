namespace SyncUpRocks.Api.Controllers;

using Microsoft.AspNetCore.Diagnostics;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        // Map specific exceptions to status codes
        var statusCode = exception switch
        {
            ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        var response = new ApiResponseBase<object>(
            Success: false,
            Data: null,
            ErrorMessage: exception.GetBaseException().GetType().ToString()
        );

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}