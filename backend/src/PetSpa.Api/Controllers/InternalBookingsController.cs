using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Services;

namespace PetSpa.Api.Controllers;

[ApiController]
[Route("api/internal/bookings")]
[Authorize]
public class InternalBookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public InternalBookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBookings(
        [FromQuery] BookingQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.GetBookingsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBookingById(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.GetBookingByIdAsync(id, cancellationToken);
        if (!result.Success)
        {
            return NotFound(result);
        }
        return Ok(result);
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmBooking(
        [FromRoute] int id,
        [FromBody] ConfirmBookingCommand command,
        CancellationToken cancellationToken)
    {
        TryGetActorId(out var actorId);
        var result = await _bookingService.ConfirmBookingAsync(id, command, actorId > 0 ? actorId : null, cancellationToken);
        return StatusCode(result.StatusCode, result.Response);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelBooking(
        [FromRoute] int id,
        [FromBody] CancelBookingCommand command,
        CancellationToken cancellationToken)
    {
        TryGetActorId(out var actorId);
        var result = await _bookingService.CancelBookingAsync(id, command, actorId > 0 ? actorId : null, cancellationToken);
        return StatusCode(result.StatusCode, result.Response);
    }

    private bool TryGetActorId(out int actorId) =>
        int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;
}
