using System.Text.Json.Serialization;

namespace PetSpa.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiErrorItem>? Errors { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Thành công") =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, string errorCode, List<ApiErrorItem>? errors = null, string traceId = "") =>
        new() { Success = false, Data = default, Message = message, ErrorCode = errorCode, Errors = errors ?? [], TraceId = traceId };
}

public class ApiErrorItem
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
