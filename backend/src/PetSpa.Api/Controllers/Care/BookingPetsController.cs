using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Controllers.Care;

[ApiController]
[Route("api/internal/booking-pets")]
[Authorize]
public class BookingPetsController(ICheckinService checkinService, ICareService careService) : ControllerBase
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpPost("{id:int}/check-in")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<BookingPet>>> CheckIn(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string? payloadJson,
        [FromForm(Name = "anhTiepNhan")] IFormFile? anhTiepNhan,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
        {
            return Unauthorized(ApiResponse<BookingPet>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Thiếu dữ liệu payload check-in.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Trường payload JSON là bắt buộc." }],
                HttpContext.TraceIdentifier));
        }

        Checkin? request;
        try
        {
            request = JsonSerializer.Deserialize<Checkin>(payloadJson, PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Payload JSON không đúng định dạng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Không thể parse JSON payload." }],
                HttpContext.TraceIdentifier));
        }

        if (request is null)
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Payload JSON không được rỗng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Payload không hợp lệ." }],
                HttpContext.TraceIdentifier));
        }

        if (anhTiepNhan is null || anhTiepNhan.Length == 0)
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Thiếu ảnh tiếp nhận thú cưng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "anhTiepNhan", Message = "File anhTiepNhan là bắt buộc." }],
                HttpContext.TraceIdentifier));
        }

        await using var ms = new MemoryStream();
        await anhTiepNhan.CopyToAsync(ms, cancellationToken);
        var photoInput = new CheckinPhotoInput(
            anhTiepNhan.FileName,
            anhTiepNhan.ContentType,
            anhTiepNhan.Length,
            ms.ToArray());

        var result = await checkinService.CheckinAsync(
            id,
            actorId,
            request,
            photoInput,
            HttpContext.TraceIdentifier,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var errorBody = ApiResponse<BookingPet>.Fail(
                result.Message,
                result.ErrorCode ?? "VALIDATION_FAILED",
                result.Errors,
                HttpContext.TraceIdentifier);

            return StatusCode(result.StatusCode, errorBody);
        }

        return Ok(ApiResponse<BookingPet>.Ok(result.Data!, result.Message));
    }

    [HttpPost("{id:int}/care-actions")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<BookingPet>>> CareActions(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string? payloadJson,
        [FromForm(Name = "anhThanhPham")] IFormFile? anhThanhPham,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
        {
            return Unauthorized(ApiResponse<BookingPet>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Thiếu dữ liệu payload cập nhật chăm sóc.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Trường payload JSON là bắt buộc." }],
                HttpContext.TraceIdentifier));
        }

        CareAction? request;
        try
        {
            request = JsonSerializer.Deserialize<CareAction>(payloadJson, PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Payload JSON không đúng định dạng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Không thể parse JSON payload." }],
                HttpContext.TraceIdentifier));
        }

        if (request is null)
        {
            return BadRequest(ApiResponse<BookingPet>.Fail(
                "Payload JSON không được rỗng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Payload không hợp lệ." }],
                HttpContext.TraceIdentifier));
        }

        CarePhotoInput? photoInput = null;
        if (anhThanhPham is not null)
        {
            await using var ms = new MemoryStream();
            await anhThanhPham.CopyToAsync(ms, cancellationToken);
            photoInput = new CarePhotoInput(
                anhThanhPham.FileName,
                anhThanhPham.ContentType,
                anhThanhPham.Length,
                ms.ToArray());
        }

        var result = await careService.ExecuteCareActionAsync(
            id,
            actorId,
            request,
            photoInput,
            HttpContext.TraceIdentifier,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var errorBody = ApiResponse<BookingPet>.Fail(
                result.Message,
                result.ErrorCode ?? "VALIDATION_FAILED",
                result.Errors,
                HttpContext.TraceIdentifier);

            return StatusCode(result.StatusCode, errorBody);
        }

        return Ok(ApiResponse<BookingPet>.Ok(result.Data!, result.Message));
    }

    [HttpPost("{id:int}/care-logs")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<CareLog>>> CareLogs(
        [FromRoute] int id,
        [FromForm(Name = "payload")] string? payloadJson,
        [FromForm(Name = "anh")] IFormFile? anh,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
        {
            return Unauthorized(ApiResponse<CareLog>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BadRequest(ApiResponse<CareLog>.Fail(
                "Thiếu dữ liệu payload nhật ký chăm sóc.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Trường payload JSON là bắt buộc." }],
                HttpContext.TraceIdentifier));
        }

        CareLogCreate? request;
        try
        {
            request = JsonSerializer.Deserialize<CareLogCreate>(payloadJson, PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse<CareLog>.Fail(
                "Payload JSON không đúng định dạng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Không thể parse JSON payload." }],
                HttpContext.TraceIdentifier));
        }

        if (request is null)
        {
            return BadRequest(ApiResponse<CareLog>.Fail(
                "Payload JSON không được rỗng.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Payload không hợp lệ." }],
                HttpContext.TraceIdentifier));
        }

        CarePhotoInput? photoInput = null;
        if (anh is not null)
        {
            await using var ms = new MemoryStream();
            await anh.CopyToAsync(ms, cancellationToken);
            photoInput = new CarePhotoInput(
                anh.FileName,
                anh.ContentType,
                anh.Length,
                ms.ToArray());
        }

        var result = await careService.CreateCareLogAsync(
            id,
            actorId,
            request,
            photoInput,
            HttpContext.TraceIdentifier,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var errorBody = ApiResponse<CareLog>.Fail(
                result.Message,
                result.ErrorCode ?? "VALIDATION_FAILED",
                result.Errors,
                HttpContext.TraceIdentifier);

            return StatusCode(result.StatusCode, errorBody);
        }

        return StatusCode(result.StatusCode, ApiResponse<CareLog>.Ok(result.Data!, result.Message));
    }
}
