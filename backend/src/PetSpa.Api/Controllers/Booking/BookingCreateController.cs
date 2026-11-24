using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
public sealed class BookingCreateController(BookingCreateService bookings, PetSpaDbContext db) : ControllerBase
{
    private const int MaximumRequestBytes = 55 * 1024 * 1024;
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true
    };

    [HttpPost("api/bookings")]
    [AllowAnonymous]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes, ValueCountLimit = 32)]
    public async Task<ActionResult<ApiResponse<BookingResponse>>> Create(CancellationToken cancellationToken)
    {
        var (request, files, error) = await ParsePayloadAsync(cancellationToken);
        if (error is not null)
            return error;

        var result = await bookings.CreateAsync(request!, files, actorId: null, cancellationToken);
        if (result.ErrorCode is not null)
            return Failure(result.ErrorCode, result.Message!);

        var response = ApiResponse<BookingResponse>.Ok(result.Data!, "Đã tiếp nhận đơn đặt lịch.");
        return result.Replayed ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("api/internal/bookings")]
    [Authorize]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes, ValueCountLimit = 32)]
    public async Task<ActionResult<ApiResponse<BookingResponse>>> CreateInternal(CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Failure("UNAUTHENTICATED", "Phiên đăng nhập không hợp lệ.");

        var staff = await db.NhanVienSet.AsNoTracking()
            .Include(s => s.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(s => s.Id == actorId && s.IsActive, cancellationToken);
        if (staff is null || (staff.VaiTro != Role.ADMIN && !staff.NhanVienQuyenNhanVienItems.Any(p => p.MaQuyen == "BOOKING_CREATE")))
            return Failure("FORBIDDEN", "Nhân viên không có quyền tạo đơn đặt lịch.");

        var (request, files, error) = await ParsePayloadAsync(cancellationToken);
        if (error is not null)
            return error;

        var result = await bookings.CreateAsync(request!, files, actorId: actorId, cancellationToken);
        if (result.ErrorCode is not null)
            return Failure(result.ErrorCode, result.Message!);

        var response = ApiResponse<BookingResponse>.Ok(result.Data!, "Đã tiếp nhận đơn đặt lịch.");
        return result.Replayed ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }

    private async Task<(BookingCreate? Request, IFormFileCollection Files, ObjectResult? Error)> ParsePayloadAsync(
        CancellationToken cancellationToken)
    {
        var contentType = Request.ContentType ?? string.Empty;
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(Request.Body, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueNonNullFields(document.RootElement))
                    return (null, new FormFileCollection(), InvalidPayload());

                var req = document.RootElement.Deserialize<BookingCreate>(PayloadOptions);
                return req is null
                    ? (null, new FormFileCollection(), InvalidPayload())
                    : (req, new FormFileCollection(), null);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                return (null, new FormFileCollection(), InvalidPayload());
            }
        }

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return (null, new FormFileCollection(), Failure("UNSUPPORTED_FILE", "Yêu cầu phải sử dụng multipart/form-data hoặc application/json."));

        if (Request.ContentLength > MaximumRequestBytes)
            return (null, new FormFileCollection(), Failure("FILE_TOO_LARGE", "Yêu cầu tải ảnh vượt quá giới hạn."));

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            if (form.Count != 1 || form["payload"] is not { Count: 1 } payload)
                return (null, form.Files, InvalidPayload());

            using var document = JsonDocument.Parse(payload[0]!, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueNonNullFields(document.RootElement))
                return (null, form.Files, InvalidPayload());

            var req = document.RootElement.Deserialize<BookingCreate>(PayloadOptions);
            return req is null
                ? (null, form.Files, InvalidPayload())
                : (req, form.Files, null);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, new FormFileCollection(), Failure("FILE_TOO_LARGE", "Yêu cầu tải ảnh vượt quá giới hạn."));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return (null, new FormFileCollection(), InvalidPayload());
        }
    }

    private bool TryActorId(out int actorId) =>
        int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;

    private ObjectResult InvalidPayload() => Failure("VALIDATION_FAILED", "Nội dung đặt lịch không hợp lệ.");

    private ObjectResult Failure(string code, string message) => StatusCode(code switch
    {
        "UNAUTHENTICATED" => StatusCodes.Status401Unauthorized,
        "FORBIDDEN" or "PROOF_SCOPE_INVALID" => StatusCodes.Status403Forbidden,
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
