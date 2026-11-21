using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
[AllowAnonymous]
[Route("api/tracking")]
public sealed class TrackingController(TrackingService tracking) : ControllerBase
{
    [HttpPost("lookup")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<ActionResult<ApiResponse<TrackingResponse>>> Lookup(
        TrackingPair pair, CancellationToken cancellationToken)
    {
        var result = await tracking.LookupAsync(pair, HttpContext.Connection.RemoteIpAddress, cancellationToken);
        if (result.Response.Success)
            return Ok(result.Response);

        result.Response.TraceId = HttpContext.TraceIdentifier;
        if (result.RetryAfterSeconds is int seconds)
        {
            Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, result.Response);
        }
        if (result.Response.ErrorCode == "TEMPORARY_FAILURE")
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result.Response);
        return NotFound(result.Response);
    }
}
