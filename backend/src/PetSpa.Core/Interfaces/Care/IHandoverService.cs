using PetSpa.Core.Common;
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
