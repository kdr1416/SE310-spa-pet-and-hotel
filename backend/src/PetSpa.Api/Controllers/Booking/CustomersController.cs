using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Booking;

[ApiController]
[Authorize]
public sealed class CustomersController(CustomerLookupService customers) : ControllerBase
{
    [HttpGet("api/internal/customers/lookup")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CustomerDto>>>> Lookup(
        [FromQuery] CustomerQuery query, CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Respond(ApiResponse<IReadOnlyList<CustomerDto>>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED"));

        return Respond(await customers.LookupByPhoneAsync(query, actorId, cancellationToken));
    }

    [HttpPatch("api/internal/pets/{petId:int}")]
    public async Task<ActionResult<ApiResponse<PetDto>>> UpdatePet(
        int petId, PetEdit request, CancellationToken cancellationToken)
    {
        if (!TryActorId(out var actorId))
            return Respond(ApiResponse<PetDto>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED"));

        if (petId <= 0)
            return Respond(ApiResponse<PetDto>.Fail("Mã thú cưng không hợp lệ.", "VALIDATION_FAILED"));

        return Respond(await customers.UpdatePetAsync(petId, request, actorId, HttpContext.TraceIdentifier, cancellationToken));
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
