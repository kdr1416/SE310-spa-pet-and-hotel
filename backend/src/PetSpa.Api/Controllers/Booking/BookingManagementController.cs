using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
[Authorize]
[Route("api/internal/bookings")]
public sealed class BookingManagementController(BookingManagementService bookings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PageBooking>>> List(
        [FromQuery] BookingQuery query, CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Respond(ApiResponse<PageBooking>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED"));
        return Respond(await bookings.ListAsync(query, actorId, cancellationToken));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<ApiResponse<BookingResponse>>> Confirm(
        int id, BookingCommand request, CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Respond(ApiResponse<BookingResponse>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED"));
        return Respond(await bookings.ConfirmAsync(id, request, actorId, HttpContext.TraceIdentifier, cancellationToken));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<ApiResponse<BookingResponse>>> Cancel(
        int id, CancelBooking request, CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Respond(ApiResponse<BookingResponse>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED"));
        return Respond(await bookings.CancelAsync(id, request, actorId, HttpContext.TraceIdentifier, cancellationToken));
    }

    private bool TryActorId(out int actorId) =>
        int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;

    private ActionResult<ApiResponse<T>> Respond<T>(ApiResponse<T> response)
    {
        if (response.Success)
            return Ok(response);

        response.TraceId = HttpContext.TraceIdentifier;
        return response.ErrorCode switch
        {
            "UNAUTHENTICATED" => Unauthorized(response),
            "FORBIDDEN" => StatusCode(StatusCodes.Status403Forbidden, response),
            "NOT_FOUND" => NotFound(response),
            "INVALID_TRANSITION" or "IDEMPOTENCY_CONFLICT" => Conflict(response),
            "TEMPORARY_FAILURE" => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}
