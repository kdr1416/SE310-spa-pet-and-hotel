namespace PetSpa.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public List<ApiErrorItem>? Errors { get; set; }
    public string TraceId { get; set; } = string.Empty;

    public static ApiResponse<T> Ok(T data, string message = "Thành công") =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, string errorCode, List<ApiErrorItem>? errors = null, string traceId = "") =>
        new() { Success = false, Data = default, Message = message, ErrorCode = errorCode, Errors = errors, TraceId = traceId };
}

public class ApiErrorItem
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
