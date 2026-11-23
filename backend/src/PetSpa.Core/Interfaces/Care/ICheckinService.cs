using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.DTOs.Identity;

namespace PetSpa.Core.Interfaces.Care;

public interface ICheckinService
{
    Task<AssignmentCandidatesResult> GetAssignmentCandidatesAsync(
        int actorId,
        ShiftQuery query,
        CancellationToken cancellationToken = default);

    Task<CheckinResult> CheckinAsync(
        int chiTietDonId,
        int actorId,
        Checkin request,
        CheckinPhotoInput photo,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<CheckinResult> ReassignAsync(
        int chiTietDonId,
        int actorId,
        AssignmentRequest request,
        string traceId,
        CancellationToken cancellationToken = default);
}

public sealed record AssignmentCandidatesResult(
    int StatusCode,
    IReadOnlyList<Employee>? Data = null,
    string Message = "",
    string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
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
