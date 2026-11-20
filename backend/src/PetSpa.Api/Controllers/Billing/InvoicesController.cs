using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/internal")]
[Authorize]
public class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    /// <summary>
    /// E23: Lập hóa đơn snapshot bất biến cho đơn đặt lịch
    /// POST /api/internal/bookings/{id}/invoices
    /// </summary>
    [HttpPost("bookings/{id:int}/invoices")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CreateInvoice(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorStaffId))
        {
            return Unauthorized(ApiResponse<InvoiceResponseDto>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await invoiceService.CreateInvoiceAsync(id, actorStaffId, cancellationToken);
        if (!result.Success)
        {
            return MapErrorResponse(result);
        }

        return StatusCode(StatusCodes.Status201Created, WithTraceId(result));
    }

    /// <summary>
    /// E24: Xem chi tiết hóa đơn
    /// GET /api/internal/invoices/{id}
    /// </summary>
    [HttpGet("invoices/{id:int}")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> GetInvoice(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorStaffId))
        {
            return Unauthorized(ApiResponse<InvoiceResponseDto>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await invoiceService.GetInvoiceByIdAsync(id, actorStaffId, cancellationToken);
        if (!result.Success)
        {
            return MapErrorResponse(result);
        }

        return Ok(WithTraceId(result));
    }

    /// <summary>
    /// E26: Xác nhận thu đủ 100% tiền và hoàn tất hóa đơn
    /// POST /api/internal/invoices/{id}/checkout
    /// </summary>
    [HttpPost("invoices/{id:int}/checkout")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CheckoutInvoice(
        [FromRoute] int id,
        [FromBody] CheckoutRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorStaffId))
        {
            return Unauthorized(ApiResponse<InvoiceResponseDto>.Fail(
                "Phiên đăng nhập không hợp lệ.",
                "UNAUTHENTICATED",
                null,
                HttpContext.TraceIdentifier));
        }

        var result = await invoiceService.CheckoutInvoiceAsync(id, request, actorStaffId, cancellationToken);
        if (!result.Success)
        {
            return MapErrorResponse(result);
        }

        return Ok(WithTraceId(result));
    }

    private bool TryGetActorId(out int actorId)
    {
        return int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;
    }

    private ActionResult<ApiResponse<InvoiceResponseDto>> MapErrorResponse(ApiResponse<InvoiceResponseDto> result)
    {
        var withTrace = WithTraceId(result);
        return result.ErrorCode switch
        {
            "FORBIDDEN" => StatusCode(StatusCodes.Status403Forbidden, withTrace),
            "BOOKING_NOT_FOUND" or "INVOICE_NOT_FOUND" => NotFound(withTrace),
            "INVOICE_ALREADY_EXISTS" or "INVALID_TRANSITION" or "AMOUNT_MISMATCH" or "IDEMPOTENCY_CONFLICT" => Conflict(withTrace),
            _ => BadRequest(withTrace)
        };
    }

    private ApiResponse<InvoiceResponseDto> WithTraceId(ApiResponse<InvoiceResponseDto> response)
    {
        response.TraceId = HttpContext.TraceIdentifier;
        return response;
    }
}
