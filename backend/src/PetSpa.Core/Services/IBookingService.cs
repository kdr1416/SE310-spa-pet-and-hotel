using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Core.Services;

public interface IBookingService
{
    Task<BookingServiceResult> CreateBookingAsync(
        BookingCreate command,
        string rawPayload,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PageBooking>> GetBookingsAsync(
        BookingQuery query,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<BookingResponse>> GetBookingByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<BookingServiceResult> ConfirmBookingAsync(
        int id,
        ConfirmBookingCommand command,
        int? actorId = null,
        CancellationToken cancellationToken = default);

    Task<BookingServiceResult> CancelBookingAsync(
        int id,
        CancelBookingCommand command,
        int? actorId = null,
        CancellationToken cancellationToken = default);
}
