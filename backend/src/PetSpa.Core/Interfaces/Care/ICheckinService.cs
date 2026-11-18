using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Core.Interfaces.Care;

public interface ICheckinService
{
    Task<CheckinResult> CheckinAsync(
        int chiTietDonId,
        int actorId,
        Checkin request,
        CheckinPhotoInput photo,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed record CheckinPhotoInput(
    string FileName,
    string ContentType,
    long Length,
    byte[] Content);

public sealed record CheckinResult(
    int StatusCode,
    BookingPet? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
