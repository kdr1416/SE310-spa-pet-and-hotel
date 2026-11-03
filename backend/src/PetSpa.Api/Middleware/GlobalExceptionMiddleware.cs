using PetSpa.Core.Common;

namespace PetSpa.Api.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var traceId = context.TraceIdentifier;
            logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);

            // Headers already sent cannot be replaced with a JSON error response.
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var response = ApiResponse<object>.Fail(
                "Đã xảy ra lỗi hệ thống nội bộ.", "INTERNAL_SERVER_ERROR", null, traceId);
            await context.Response.WriteAsJsonAsync(response, options: null, contentType: "application/json");
        }
    }
}
