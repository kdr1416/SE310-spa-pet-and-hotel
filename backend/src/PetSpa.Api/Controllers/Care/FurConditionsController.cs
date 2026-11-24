using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Care;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Api.Controllers.Care;

[ApiController]
[Authorize]
[Route("api/internal/fur-conditions")]
public sealed class FurConditionsController(FurConditionsService conditions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FurConditionItem>>>> Get(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
            return Unauthorized(ApiResponse<object>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED", traceId: HttpContext.TraceIdentifier));
        var response = await conditions.GetAsync(actorId, cancellationToken);
        if (response.Success) return Ok(response);
        response.TraceId = HttpContext.TraceIdentifier;
        return StatusCode(response.ErrorCode == "FORBIDDEN" ? 403 : 503, response);
    }
}
