using Microsoft.AspNetCore.Mvc;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.Services;

namespace PetSpa.Api.Controllers;

[ApiController]
[Route("api/availability")]
public class AvailabilityController(IAvailabilityService availability) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AvailabilityResponse>>> CheckAvailability(
        [FromBody] AvailabilityQuery query, CancellationToken cancellationToken)
    {
        var check = await availability.CheckAvailabilityAsync(query, cancellationToken);
        if (check.ErrorCode is null)
            return Ok(ApiResponse<AvailabilityResponse>.Ok(check.Data!, "Ca làm việc còn khả dụng."));

        var error = ApiResponse<AvailabilityResponse>.Fail(
            check.Message!, check.ErrorCode, traceId: HttpContext.TraceIdentifier);
        return check.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(error),
            "CAPACITY_EXCEEDED" => Conflict(error),
            _ => BadRequest(error)
        };
    }
}
