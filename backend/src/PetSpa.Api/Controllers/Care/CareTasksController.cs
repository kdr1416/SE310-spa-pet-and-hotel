using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Care;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Api.Controllers.Care;

[ApiController]
[Authorize]
[Route("api/internal/care-tasks")]
public sealed class CareTasksController(CareTasksService tasks) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PageBookingPet>>> List([FromQuery] BookingQuery query, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
            return Unauthorized(ApiResponse<object>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED", traceId: HttpContext.TraceIdentifier));
        var response = await tasks.ListAsync(query, actorId, cancellationToken);
        if (response.Success) return Ok(response);
        response.TraceId = HttpContext.TraceIdentifier;
        return StatusCode(response.ErrorCode switch { "FORBIDDEN" => 403, "TEMPORARY_FAILURE" => 503, _ => 400 }, response);
    }
}
