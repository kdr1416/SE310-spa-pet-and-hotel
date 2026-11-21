using PetSpa.Core.DTOs.Availability;

namespace PetSpa.Core.Services;

public interface IAvailabilityService
{
    Task<AvailabilityCheck> CheckAvailabilityAsync(AvailabilityQuery query, CancellationToken cancellationToken = default);
}

public sealed record AvailabilityCheck(AvailabilityResponse? Data, string? ErrorCode = null, string? Message = null);
