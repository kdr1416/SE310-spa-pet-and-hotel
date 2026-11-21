using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
[AllowAnonymous]
[Route("api/bookings")]
public sealed class BookingCreateController(BookingCreateService bookings) : ControllerBase
{
    private const int MaximumRequestBytes = 55 * 1024 * 1024;
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true
    };

    [HttpPost]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes, ValueCountLimit = 32)]
    public async Task<ActionResult<ApiResponse<BookingResponse>>> Create(CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return Failure("UNSUPPORTED_FILE", "Yêu cầu phải sử dụng multipart/form-data.");
        if (Request.ContentLength > MaximumRequestBytes)
            return Failure("FILE_TOO_LARGE", "Yêu cầu tải ảnh vượt quá giới hạn.");

        IFormCollection form;
        BookingCreate? request;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
            if (form.Count != 1 || form["payload"] is not { Count: 1 } payload)
                return InvalidPayload();

            using var document = JsonDocument.Parse(payload[0]!, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueNonNullFields(document.RootElement))
                return InvalidPayload();

            request = document.RootElement.Deserialize<BookingCreate>(PayloadOptions);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Failure("FILE_TOO_LARGE", "Yêu cầu tải ảnh vượt quá giới hạn.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return InvalidPayload();
        }

        if (request is null)
            return InvalidPayload();

        var result = await bookings.CreateAsync(request, form.Files, cancellationToken);
        if (result.ErrorCode is not null)
            return Failure(result.ErrorCode, result.Message!);

        var response = ApiResponse<BookingResponse>.Ok(result.Data!, "Đã tiếp nhận đơn đặt lịch.");
        return result.Replayed ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }

    private ObjectResult InvalidPayload() => Failure("VALIDATION_FAILED", "Nội dung đặt lịch không hợp lệ.");

    private ObjectResult Failure(string code, string message) => StatusCode(code switch
    {
        "PROOF_SCOPE_INVALID" => StatusCodes.Status403Forbidden,
        "NOT_FOUND" => StatusCodes.Status404NotFound,
        "CAPACITY_EXCEEDED" or "IDEMPOTENCY_CONFLICT" => StatusCodes.Status409Conflict,
        "FILE_TOO_LARGE" => StatusCodes.Status413PayloadTooLarge,
        "UNSUPPORTED_FILE" => StatusCodes.Status415UnsupportedMediaType,
        "TEMPORARY_FAILURE" or "STORAGE_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    }, ApiResponse<BookingResponse>.Fail(message, code, traceId: HttpContext.TraceIdentifier));

    private static bool HasUniqueNonNullFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return element.EnumerateObject().All(property => names.Add(property.Name) && HasUniqueNonNullFields(property.Value));
        }

        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray().All(HasUniqueNonNullFields),
            JsonValueKind.Null => false,
            _ => true
        };
    }
}
