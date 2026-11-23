using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Core.Interfaces.Care;

public interface ICareService
{
    Task<CareActionResult> ExecuteCareActionAsync(
        int chiTietDonId,
        int actorId,
        CareAction request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<CareLogCreateResult> CreateCareLogAsync(
        int chiTietDonId,
        int actorId,
        CareLogCreate request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<OrderServiceActionResult> CompleteComboComponentAsync(
        int orderServiceId,
        int componentId,
        int actorId,
        BookingCommand request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<OrderServiceActionResult> ExecuteExtraServiceActionAsync(
        int orderServiceId,
        int actorId,
        ServiceActionRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed record CarePhotoInput(
    string FileName,
    string ContentType,
    long Length,
    byte[] Content);

public sealed record CareActionResult(
    int StatusCode,
    BookingPet? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}

public sealed record CareLogCreateResult(
    int StatusCode,
    CareLog? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}

public sealed record OrderServiceActionResult(
    int StatusCode,
    OrderService? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
