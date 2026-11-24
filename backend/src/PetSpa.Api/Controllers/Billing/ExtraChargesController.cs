using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/internal")]
[Authorize]
public class ExtraChargesController(IExtraChargeService extraCharges) : ControllerBase
{
    /// <summary>
    /// E20: Thêm dịch vụ / combo phát sinh cho thú cưng
    /// POST /api/internal/booking-pets/{id}/extra-services
    /// </summary>
    [HttpPost("booking-pets/{id:int}/extra-services")]
    public async Task<ActionResult<ApiResponse<OrderService>>> AddExtraService(
        int id, [FromBody] ExtraServiceRequest request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<OrderService>();

        var result = await extraCharges.AddExtraServiceAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>
    /// E22a: Thêm phụ phí cho đơn đặt lịch (hoặc riêng từng thú cưng)
    /// POST /api/internal/bookings/{id}/fees
    /// </summary>
    [HttpPost("bookings/{id:int}/fees")]
    public async Task<ActionResult<ApiResponse<FeeDto>>> CreateFee(
        int id, [FromBody] FeeCreate request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<FeeDto>();

        var result = await extraCharges.CreateFeeAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>
    /// E22b: Cập nhật tên hoặc số tiền phụ phí
    /// PATCH /api/internal/fees/{id}
    /// </summary>
    [HttpPatch("fees/{id:int}")]
    public async Task<ActionResult<ApiResponse<FeeDto>>> EditFee(
        int id, [FromBody] FeeEdit request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<FeeDto>();

        var result = await extraCharges.EditFeeAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, StatusCodes.Status200OK);
    }

    /// <summary>
    /// E22c: Hủy phụ phí chưa thanh toán
    /// POST /api/internal/fees/{id}/cancel
    /// </summary>
    [HttpPost("fees/{id:int}/cancel")]
    public async Task<ActionResult<ApiResponse<FeeDto>>> CancelFee(
        int id, [FromBody] ReasonCommand request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<FeeDto>();

        var result = await extraCharges.CancelFeeAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, StatusCodes.Status200OK);
    }

    private string TraceId => HttpContext.TraceIdentifier[..Math.Min(40, HttpContext.TraceIdentifier.Length)];

    private bool TryGetActorId(out int actorId) =>
        int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;

    private ActionResult<ApiResponse<T>> Unauthenticated<T>() =>
        Unauthorized(ApiResponse<T>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED", [], TraceId));

    private ActionResult<ApiResponse<T>> Respond<T>(
        ApiResponse<T> response, int successStatusCode = StatusCodes.Status200OK)
    {
        if (response.Success)
            return StatusCode(successStatusCode, response);

        response.TraceId = TraceId;
        return response.ErrorCode switch
        {
            "FORBIDDEN" => StatusCode(StatusCodes.Status403Forbidden, response),
            "NOT_FOUND" => NotFound(response),
            "INVALID_TRANSITION" or "CAPACITY_EXCEEDED" or "IDEMPOTENCY_CONFLICT" => Conflict(response),
            "TEMPORARY_FAILURE" => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}