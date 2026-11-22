using PetSpa.Core.Entities.Booking;

namespace PetSpa.Core.Interfaces.Billing;

public interface IDepositExpiryService
{
    Task<int> ExpireOverdueDepositsAsync(DateTimeOffset now, int? targetBookingId, CancellationToken ct,
        IReadOnlyCollection<int>? targetKhungGioIds = null, IReadOnlyCollection<int>? targetRoomTypeIds = null);
    Task ExpireLockedBookingDepositAsync(DonDatLich booking, DateTimeOffset now, CancellationToken ct);
}
