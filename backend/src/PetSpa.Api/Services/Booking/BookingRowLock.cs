using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;

namespace PetSpa.Api.Services.Booking;

internal static class BookingRowLock
{
    // Use inside a transaction and before taking any locks on slots, pets or invoices for the booking.
    public static async Task<bool> AcquireAsync(PetSpaDbContext db, int bookingId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await db.DonDatLichSet.AnyAsync(b => b.Id == bookingId, cancellationToken);

        var rows = await db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.Count == 1;
    }
}
