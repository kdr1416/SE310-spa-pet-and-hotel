using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSpa.Api.Services.Scheduling;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Scheduling;

namespace PetSpa.Api.Controllers.Scheduling;

[ApiController]
[Authorize]
public sealed class WorkScheduleController(WorkScheduleService service) : ControllerBase
{
    [HttpGet("/api/internal/work-schedule")]
    public async Task<IActionResult> List([FromQuery] string? tuNgay, [FromQuery] string? denNgay,
        [FromQuery] int? maNhanVien, CancellationToken ct)
    {
        if (!TryActor(out var actorId)) return Unauthenticated();
        var errors = new List<ApiErrorItem>();
        if (!DateOnly.TryParseExact(tuNgay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            errors.Add(new() { Field = "tuNgay", Message = "Ngày bắt đầu phải có định dạng yyyy-MM-dd." });
        if (!DateOnly.TryParseExact(denNgay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            errors.Add(new() { Field = "denNgay", Message = "Ngày kết thúc phải có định dạng yyyy-MM-dd." });
        if (maNhanVien is <= 0 || (Request.Query.ContainsKey("maNhanVien") && !maNhanVien.HasValue))
            errors.Add(new() { Field = "maNhanVien", Message = "ID nhân viên phải lớn hơn 0." });
        if (errors.Count > 0)
            return Respond(new ScheduleResult<object>(400, ErrorCode: "VALIDATION_FAILED", Message: "Dữ liệu không hợp lệ.", Errors: errors));
        // Construct the existing DTO after parsing, so range errors remain INVALID_DATE_RANGE.
        var query = new ScheduleQuery { TuNgay = from, DenNgay = to };
        if (maNhanVien.HasValue) query = new ScheduleQuery { TuNgay = from, DenNgay = to, MaNhanVien = maNhanVien.Value };
        return Respond(await service.ListAsync(actorId, query, ct));
    }

    private bool TryActor(out int id) => int.TryParse(User.FindFirst("id")?.Value, out id) && id > 0;

    [HttpPost("/api/admin/shift-windows")]
    public async Task<IActionResult> CreateWindow([FromBody] ShiftWindowWriteRequest request, CancellationToken ct)
    {
        if (!TryActor(out var actorId)) return Unauthenticated();
        return Respond(await service.CreateWindowAsync(actorId, request, HttpContext.TraceIdentifier, ct));
    }

    [HttpPost("/api/internal/shift-registrations")]
    public async Task<IActionResult> Register([FromBody] ShiftRegisterRequest request, CancellationToken ct)
    {
        if (!TryActor(out var actorId)) return Unauthenticated();
        return Respond(await service.RegisterAsync(actorId, request, HttpContext.TraceIdentifier, ct));
    }

    [HttpPost("/api/admin/shift-registrations/{id}/decision")]
    public async Task<IActionResult> Decide([FromRoute] int id, [FromBody] ShiftDecisionRequest request, CancellationToken ct)
    {
        if (!TryActor(out var actorId)) return Unauthenticated();
        if (id <= 0) return InvalidId();
        return Respond(await service.DecideAsync(actorId, id, request, HttpContext.TraceIdentifier, ct));
    }

    private IActionResult InvalidId() => Respond(new ScheduleResult<object>(400, Message: "ID phải lớn hơn 0.",
        ErrorCode: "VALIDATION_FAILED", Errors: [new() { Field = "id", Message = "ID phải lớn hơn 0." }]));

    [HttpPost("/api/admin/shift-registrations/{id}/transfer")]
    public async Task<IActionResult> Transfer([FromRoute] int id, [FromBody] ShiftTransferRequest request, CancellationToken ct)
    {
        if (!TryActor(out var actorId)) return Unauthenticated();
        if (id <= 0) return InvalidId();
        return Respond(await service.TransferAsync(actorId, id, request, HttpContext.TraceIdentifier, ct));
    }
    private IActionResult Unauthenticated() => Unauthorized(ApiResponse<object>.Fail(
        "Phiên đăng nhập không hợp lệ.", "UNAUTHENTICATED", traceId: HttpContext.TraceIdentifier));
    private IActionResult Respond<T>(ScheduleResult<T> result) => StatusCode(result.StatusCode,
        result.StatusCode is >= 200 and < 300
            ? ApiResponse<T>.Ok(result.Data!, result.Message)
            : ApiResponse<T>.Fail(result.Message, result.ErrorCode!, result.Errors, HttpContext.TraceIdentifier));
}
