using PetSpa.Core.Entities.Booking;

namespace PetSpa.Core.Interfaces.Billing;

public interface IBillingSettlementHelper
{
    void SettleCancelledPetInMemory(DonDatLich booking, ChiTietDon pet, IReadOnlySet<int> completedComponentIds,
        int nightsStayed, DateTimeOffset now);
    decimal CalculateObligatedDepositAmount(DonDatLich booking);
    void TryTransitionBookingTerminalState(DonDatLich booking, DateTimeOffset now);
}
