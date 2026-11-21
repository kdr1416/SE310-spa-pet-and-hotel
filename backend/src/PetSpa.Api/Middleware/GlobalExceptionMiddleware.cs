using PetSpa.Core.Common;
using PetSpa.Api.Services.Storage;

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
            var (status, message, code) = exception switch
            {
                FileStorageException { ErrorCode: "FILE_TOO_LARGE" } storage =>
                    (StatusCodes.Status413PayloadTooLarge, storage.Message, storage.ErrorCode),
                FileStorageException { ErrorCode: "UNSUPPORTED_FILE" } storage =>
                    (StatusCodes.Status415UnsupportedMediaType, storage.Message, storage.ErrorCode),
                FileStorageException { ErrorCode: "STORAGE_UNAVAILABLE" } storage =>
                    (StatusCodes.Status503ServiceUnavailable, storage.Message, storage.ErrorCode),
                _ => (StatusCodes.Status500InternalServerError, "Đã xảy ra lỗi hệ thống nội bộ.", "INTERNAL_SERVER_ERROR")
            };
            context.Response.StatusCode = status;
            var response = ApiResponse<object>.Fail(message, code, traceId: traceId);
            await context.Response.WriteAsJsonAsync(response, options: null, contentType: "application/json");
        }
    }
}
