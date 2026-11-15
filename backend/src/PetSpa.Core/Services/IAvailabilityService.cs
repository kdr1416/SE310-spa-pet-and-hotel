using PetSpa.Core.DTOs.Availability ;
namespace PetSpa.Core.Services ;
public interface IAvailabilityService
{
    Task<AvailabilityResponse> CheckAvailabilityAsync(
        AvailabilityQuery query,
        CancellationToken cancellationToken =default
    );
}