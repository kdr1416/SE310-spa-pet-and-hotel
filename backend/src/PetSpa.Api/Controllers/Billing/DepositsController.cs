using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Controllers.Billing;

[ApiController]
[Route("api")]
[Authorize]
public class DepositsController(IDepositService deposits) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// E42: Ghi nhận thu tiền cọc 30%
    /// POST /api/internal/deposits/{id}/receipts
    /// </summary>
    [HttpPost("internal/deposits/{id:int}/receipts")]
    public async Task<ActionResult<ApiResponse<DepositDto>>> ConfirmReceipt(
        int id, [FromBody] ReceiptCreate request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<DepositDto>();

        var result = await deposits.ConfirmDepositReceiptAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>
    /// E43: Danh sách yêu cầu thu cọc (phân trang & lọc)
    /// GET /api/internal/deposits
    /// </summary>
    [HttpGet("internal/deposits")]
    public async Task<ActionResult<ApiResponse<PageDeposit>>> GetDeposits(
        [FromQuery] BookingQuery query, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<PageDeposit>();

        return Respond(await deposits.GetDepositsAsync(query, actor, ct));
    }

    /// <summary>
    /// E44: Admin tạo lệnh hoàn tiền cọc
    /// POST /api/admin/deposits/{id}/refunds
    /// </summary>
    [HttpPost("admin/deposits/{id:int}/refunds")]
    public async Task<ActionResult<ApiResponse<RefundDto>>> CreateRefund(
        int id, [FromBody] RefundCreate request, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<RefundDto>();

        var result = await deposits.CreateRefundAsync(id, request, actor, TraceId, ct);
        return Respond(result.Response, result.IsReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }

    /// <summary>
    /// E45: Admin xác nhận đã hoàn tiền cọc (hỗ trợ application/json và multipart/form-data)
    /// POST /api/admin/refunds/{id}/confirm
    /// </summary>
    [HttpPost("admin/refunds/{id:int}/confirm")]
    public async Task<ActionResult<ApiResponse<RefundDto>>> ConfirmRefund(int id, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<RefundDto>();

        RefundConfirm? request;
        byte[]? evidenceBytes = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            var payloadJson = form["payload"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return Respond(ApiResponse<RefundDto>.Fail("Thiếu trường payload JSON trong form-data.", "VALIDATION_FAILED"));
            }

            try
            {
                request = JsonSerializer.Deserialize<RefundConfirm>(payloadJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                return Respond(ApiResponse<RefundDto>.Fail($"Dữ liệu payload không hợp lệ: {ex.Message}", "VALIDATION_FAILED"));
            }

            var file = form.Files.GetFile("bangChung") ?? form.Files.FirstOrDefault();
            if (file is { Length: > 0 })
            {
                if (file.Length > 5 * 1024 * 1024)
                    return Respond(ApiResponse<RefundDto>.Fail("File bằng chứng vượt quá 5MB.", "VALIDATION_FAILED"));

                await using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                evidenceBytes = ms.ToArray();
            }
        }
        else
        {
            try
            {
                request = await JsonSerializer.DeserializeAsync<RefundConfirm>(Request.Body, JsonOptions, ct);
            }
            catch (JsonException ex)
            {
                return Respond(ApiResponse<RefundDto>.Fail($"Dữ liệu JSON không hợp lệ: {ex.Message}", "VALIDATION_FAILED"));
            }
        }

        if (request is null)
            return Respond(ApiResponse<RefundDto>.Fail("Thiếu dữ liệu xác nhận hoàn cọc.", "VALIDATION_FAILED"));

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
        {
            var errors = validationResults
                .Select(v => new ApiErrorItem
                {
                    Field = v.MemberNames.FirstOrDefault() ?? string.Empty,
                    Message = v.ErrorMessage ?? "Không hợp lệ"
                })
                .ToList();
            return Respond(ApiResponse<RefundDto>.Fail("Dữ liệu xác nhận hoàn cọc không hợp lệ.", "VALIDATION_FAILED", errors));
        }

        var result = await deposits.ConfirmRefundAsync(id, request, evidenceBytes, actor, TraceId, ct);
        return Respond(result.Response, StatusCodes.Status200OK);
    }

    /// <summary>
    /// E46: Admin xem danh sách lệnh hoàn cọc (phân trang & lọc)
    /// GET /api/admin/refunds
    /// </summary>
    [HttpGet("admin/refunds")]
    public async Task<ActionResult<ApiResponse<PageRefund>>> GetRefunds(
        [FromQuery] BookingQuery query, CancellationToken ct)
    {
        if (!TryGetActorId(out var actor))
            return Unauthenticated<PageRefund>();

        return Respond(await deposits.GetRefundsAsync(query, actor, ct));
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
            "INVALID_TRANSITION" or "AMOUNT_MISMATCH" or "IDEMPOTENCY_CONFLICT" => Conflict(response),
            "TEMPORARY_FAILURE" => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}