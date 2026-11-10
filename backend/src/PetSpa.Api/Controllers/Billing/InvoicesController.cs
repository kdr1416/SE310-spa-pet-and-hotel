using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/internal")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    /// <summary>
    /// E23: Lập hóa đơn snapshot bất biến cho đơn đặt lịch
    /// POST /api/internal/bookings/{id}/invoices
    /// </summary>
    [HttpPost("bookings/{id}/invoices")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CreateInvoice(int id)
    {
        // Sprint 1 dùng tạm ActorId = 1 (Seed Staff/Admin)
        int actorStaffId = 1;
        var result = await _invoiceService.CreateInvoiceAsync(id, actorStaffId);

        if (!result.Success)
        {
            if (result.ErrorCode == "INVOICE_ALREADY_EXISTS" || result.ErrorCode == "INVALID_TRANSITION")
                return Conflict(result);
            if (result.ErrorCode == "BOOKING_NOT_FOUND")
                return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// E24: Xem chi tiết hóa đơn
    /// GET /api/internal/invoices/{id}
    /// </summary>
    [HttpGet("invoices/{id}")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> GetInvoice(int id)
    {
        var result = await _invoiceService.GetInvoiceByIdAsync(id);
        if (!result.Success && result.ErrorCode == "INVOICE_NOT_FOUND")
        {
            return NotFound(result);
        }
        return Ok(result);
    }

    /// <summary>
    /// E26: Xác nhận thu đủ 100% tiền và hoàn tất hóa đơn
    /// POST /api/internal/invoices/{id}/checkout
    /// </summary>
    [HttpPost("invoices/{id}/checkout")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CheckoutInvoice(int id, [FromBody] CheckoutRequestDto request)
    {
        int actorStaffId = 1;
        var result = await _invoiceService.CheckoutInvoiceAsync(id, request, actorStaffId);

        if (!result.Success)
        {
            if (result.ErrorCode is "AMOUNT_MISMATCH" or "INVALID_TRANSITION" or "IDEMPOTENCY_CONFLICT")
                return Conflict(result);
            if (result.ErrorCode == "INVOICE_NOT_FOUND")
                return NotFound(result);
        }

        return Ok(result);
    }
}