using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Core.Interfaces.Care;

public interface IHandoverService
{
    Task<HandoverResult> HandoverAsync(
        int chiTietDonId,
        int actorId,
        Handover request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<HandoverResult> CancelPetAsync(
        int chiTietDonId,
        int actorId,
        PetCancelRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<HandoverResult> EarlyHandoverAsync(
        int chiTietDonId,
        int actorId,
        ReasonCommand request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<HandoverResult> ReturnCancelledPetAsync(
        int chiTietDonId,
        int actorId,
        BookingCommand request,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed record HandoverResult(
    int StatusCode,
    BookingPet? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
