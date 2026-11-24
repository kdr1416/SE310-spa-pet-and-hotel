using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api/internal")]
[Authorize]
public class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    [HttpPost("bookings/{id:int}/invoices")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CreateInvoice(
        int id, [FromBody] CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated();

        var result = await invoices.CreateInvoiceAsync(id, request, actor, TraceId, cancellationToken);
        return Respond(result.Response, result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    [HttpGet("invoices/{id:int}")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> GetInvoice(int id, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated();

        return Respond(await invoices.GetInvoiceByIdAsync(id, actor, cancellationToken));
    }

    [HttpPost("invoices/{id:int}/checkout")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> CheckoutInvoice(
        int id, [FromBody] CheckoutRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated();

        return Respond(await invoices.CheckoutInvoiceAsync(id, request, actor, TraceId, cancellationToken));
    }

    /// <summary>
    /// E25: Thu hồi hóa đơn UNPAID sang REVOKED
    /// POST /api/internal/invoices/{id}/withdraw
    /// </summary>
    [HttpPost("invoices/{id:int}/withdraw")]
    public async Task<ActionResult<ApiResponse<InvoiceResponseDto>>> WithdrawInvoice(
        int id, [FromBody] ReasonCommand request, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated();

        return Respond(await invoices.WithdrawInvoiceAsync(id, request, actor, TraceId, cancellationToken));
    }

    private string TraceId => HttpContext.TraceIdentifier[..Math.Min(40, HttpContext.TraceIdentifier.Length)];

    private bool TryGetActorId(out int actorId) =>
        int.TryParse(User.FindFirst("id")?.Value, out actorId) && actorId > 0;

    private ActionResult<ApiResponse<InvoiceResponseDto>> Unauthenticated() =>
        Unauthorized(ApiResponse<InvoiceResponseDto>.Fail("Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED", [], TraceId));

    private ActionResult<ApiResponse<InvoiceResponseDto>> Respond(
        ApiResponse<InvoiceResponseDto> response, int successStatusCode = StatusCodes.Status200OK)
    {
        if (response.Success)
            return StatusCode(successStatusCode, response);

        response.TraceId = TraceId;
        return response.ErrorCode switch
        {
            "FORBIDDEN" => StatusCode(StatusCodes.Status403Forbidden, response),
            "NOT_FOUND" => NotFound(response),
            "INVALID_TRANSITION" or "AMOUNT_MISMATCH" or "IDEMPOTENCY_CONFLICT" => Conflict(response),
            "TEMPORARY_FAILURE" => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}