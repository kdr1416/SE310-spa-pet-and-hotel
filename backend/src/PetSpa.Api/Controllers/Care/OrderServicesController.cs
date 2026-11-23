using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Controllers.Care;

[ApiController]
[Route("api/internal")]
[Authorize]
public class OrderServicesController(ICareService careService) : ControllerBase
{
    [HttpPost("order-services/{id:int}/components/{componentId:int}/complete")]
    public async Task<ActionResult<ApiResponse<OrderService>>> CompleteComboComponent(
        [FromRoute] int id,
        [FromRoute] int componentId,
        [FromBody] BookingCommand request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
        {
            return Unauthorized(ApiResponse<OrderService>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await careService.CompleteComboComponentAsync(
            id,
            componentId,
            actorId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var errorBody = ApiResponse<OrderService>.Fail(
                result.Message,
                result.ErrorCode ?? "VALIDATION_FAILED",
                result.Errors,
                HttpContext.TraceIdentifier);

            return StatusCode(result.StatusCode, errorBody);
        }

        return Ok(ApiResponse<OrderService>.Ok(result.Data!, result.Message));
    }

    [HttpPost("extra-services/{id:int}/actions")]
    public async Task<ActionResult<ApiResponse<OrderService>>> ExecuteExtraServiceAction(
        [FromRoute] int id,
        [FromBody] ServiceActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var actorId) || actorId <= 0)
        {
            return Unauthorized(ApiResponse<OrderService>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await careService.ExecuteExtraServiceActionAsync(
            id,
            actorId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var errorBody = ApiResponse<OrderService>.Fail(
                result.Message,
                result.ErrorCode ?? "VALIDATION_FAILED",
                result.Errors,
                HttpContext.TraceIdentifier);

            return StatusCode(result.StatusCode, errorBody);
        }

        return Ok(ApiResponse<OrderService>.Ok(result.Data!, result.Message));
    }
}
