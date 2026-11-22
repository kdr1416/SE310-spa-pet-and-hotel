using Microsoft.EntityFrameworkCore;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;
using PetSpa.Tests.Care;

namespace PetSpa.Tests.Foundation;

public sealed class DepositExpiryConcurrencyTests
{
    private static readonly DateTimeOffset Now = BookingDepositProjectionTests.Now;

    [Fact]
    public async Task ConcurrentSweeps_ReleaseAndRefundExactlyOnce()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        int id;
        await using (var db = fixture.Database.CreateContext())
        {
            var booking = await fixture.AddBookingAsync(db);
            id = booking.Id;
            booking.TienCocDonDatLich!.GiaoDichThuTienCocItems.Add(DepositExpiryTests.Receipt(20000m, await db.NhanVienSet.Select(x => x.Id).FirstAsync(), Now));
            await db.SaveChangesAsync();
        }
        await using var first = fixture.Database.CreateContext();
        await using var second = fixture.Database.CreateContext();
        var counts = await Task.WhenAll(DepositExpiryTests.Service(fixture, first).ExpireOverdueDepositsAsync(Now, id, default),
            DepositExpiryTests.Service(fixture, second).ExpireOverdueDepositsAsync(Now, id, default));
        Assert.Equal(1, counts.Sum());
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(20000m, (await verify.HoanCocSet.SingleAsync()).SoTien);
        Assert.Equal(0, (await verify.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync()).SlotDaDat);
        Assert.Equal(0, (await verify.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync()).PhutDaGiu);
    }

    [Fact]
    public async Task ConfirmedWhileExpiryWaits_IsRecheckedAfterLock()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var blocker = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(blocker);
        await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id={booking.Id} FOR UPDATE").ToListAsync();
        await using var sweeper = fixture.Database.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var expiry = DepositExpiryTests.Service(fixture, sweeper).ExpireOverdueDepositsAsync(Now, booking.Id, timeout.Token);
        await CareTestSupport.WaitForLockWaitersAsync(fixture.Database.ConnectionString, "%FROM don_dat_lich%FOR UPDATE%", 1, timeout.Token);
        booking.TrangThai = BookingState.CONFIRMED;
        booking.TienCocDonDatLich!.TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG;
        await blocker.SaveChangesAsync(timeout.Token);
        await tx.CommitAsync(timeout.Token);
        Assert.Equal(0, await expiry);
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(BookingState.CONFIRMED, (await verify.DonDatLichSet.SingleAsync()).TrangThai);
        Assert.Equal(1, (await verify.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync()).SlotDaDat);
    }

    [Fact]
    public async Task ReceiptConfirmationWaitingPastDeadline_CannotKeepReservation()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now.AddMinutes(-1));
        int id;
        await using (var seed = fixture.Database.CreateContext())
        {
            var booking = await fixture.AddBookingAsync(seed);
            booking.TienCocDonDatLich!.HetHanAt = Now;
            await seed.SaveChangesAsync();
            id = booking.Id;
        }
        await using var blocker = fixture.Database.CreateContext();
        await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id={id} FOR UPDATE").ToListAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var confirmation = ConfirmAfterLockAsync();
        await CareTestSupport.WaitForLockWaitersAsync(fixture.Database.ConnectionString, "%FROM don_dat_lich%FOR UPDATE%", 1, timeout.Token);
        fixture.Clock.UtcNow = Now;
        await tx.CommitAsync(timeout.Token);
        await confirmation;
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(BookingState.CANCELLED, (await verify.DonDatLichSet.SingleAsync()).TrangThai);
        Assert.Equal(DepositRequestState.HET_HAN, (await verify.TienCocSet.SingleAsync()).TrangThaiYeuCau);
        Assert.Equal(45000m, (await verify.HoanCocSet.SingleAsync()).SoTien);

        async Task ConfirmAfterLockAsync()
        {
            await using var db = fixture.Database.CreateContext();
            await using var own = await db.Database.BeginTransactionAsync(timeout.Token);
            var locked = await DepositExpiryTests.LockBookingAsync(db, id);
            var actor = await db.NhanVienSet.Select(x => x.Id).FirstAsync(timeout.Token);
            locked.TienCocDonDatLich!.GiaoDichThuTienCocItems.Add(DepositExpiryTests.Receipt(45000m, actor, fixture.Clock.GetUtcNow()));
            await DepositExpiryTests.Service(fixture, db).ExpireLockedBookingDepositAsync(locked, fixture.Clock.GetUtcNow(), timeout.Token);
            await db.SaveChangesAsync(timeout.Token);
            await own.CommitAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task WriterProtocol_NewHoldExpiresWhileWaiting_ReleasesLocksThenCleansAgain()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now.AddMinutes(-1));
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        booking.TienCocDonDatLich!.HetHanAt = Now;
        await db.SaveChangesAsync();
        var service = DepositExpiryTests.Service(fixture, db);
        Assert.Equal(0, await service.ExpireOverdueDepositsAsync(fixture.Clock.GetUtcNow(), null, default, [booking.KhungGioId]));
        await using (var main = await db.Database.BeginTransactionAsync())
        {
            await db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM khung_gio_dat_lich WHERE id={booking.KhungGioId} FOR UPDATE").ToListAsync();
            fixture.Clock.UtcNow = Now;
            await main.RollbackAsync();
        }
        Assert.Equal(1, await service.ExpireOverdueDepositsAsync(fixture.Clock.GetUtcNow(), null, default, [booking.KhungGioId]));
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(0, (await verify.KhungGioDatLichSet.SingleAsync(x => x.Id == booking.KhungGioId)).SlotDaDat);
    }
}
