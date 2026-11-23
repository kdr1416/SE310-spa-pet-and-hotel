using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
[Route("api/booking-options")]
public sealed class BookingOptionsController(BookingOptionsService options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<BookingOptions>>> Get(
        [FromQuery] BookingOptionsQuery query, CancellationToken cancellationToken)
    {
        var response = await options.GetAsync(query.Ngay!.Value, cancellationToken);
        if (response.Success) return Ok(response);
        response.TraceId = HttpContext.TraceIdentifier;
        return BadRequest(response);
    }
}
