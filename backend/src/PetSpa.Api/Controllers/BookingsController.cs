using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Services;

namespace PetSpa.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateBooking(
        [FromForm(Name = "payload")] string? payloadJson,
        [FromForm(Name = "vaccinationFile")] IFormFile? vaccinationFile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return BadRequest(ApiResponse<BookingResponse>.Fail(
                "Thiếu dữ liệu payload đặt lịch.",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = "Trường payload JSON là bắt buộc." }],
                HttpContext.TraceIdentifier));
        }

        BookingCreate? command;
        try
        {
            command = JsonSerializer.Deserialize<BookingCreate>(payloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return BadRequest(ApiResponse<BookingResponse>.Fail(
                $"Định dạng payload không hợp lệ: {ex.Message}",
                "VALIDATION_FAILED",
                [new ApiErrorItem { Field = "payload", Message = ex.Message }],
                HttpContext.TraceIdentifier));
        }

        if (command == null)
        {
            return BadRequest(ApiResponse<BookingResponse>.Fail(
                "Không thể đọc thông tin đặt lịch từ payload.",
                "VALIDATION_FAILED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await _bookingService.CreateBookingAsync(command, payloadJson, cancellationToken);
        return StatusCode(result.StatusCode, result.Response);
    }
}
