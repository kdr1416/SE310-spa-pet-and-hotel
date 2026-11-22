using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Tests.Foundation;

public sealed class DepositExpiryTests
{
    private static readonly DateTimeOffset Now = BookingDepositProjectionTests.Now;

    [Fact]
    public async Task Expiry_AtDeadline_CancelsBookingReleasesSpaAndRefundsExactlyOnce()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        var actors = await db.NhanVienSet.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var deposit = booking.TienCocDonDatLich!;
        deposit.GiaoDichThuTienCocItems.Add(Receipt(80000m, actors[0], Now.AddMinutes(-2)));
        deposit.GiaoDichThuTienCocItems.Add(Receipt(20000m, actors[1], Now.AddMinutes(-1)));
        deposit.HoanCocTienCocItems.Add(new HoanCoc { RequestId = Guid.NewGuid(), SoTien = 30000m,
            LyDo = "Đã dành hoàn", TaoBoiId = actors[0], TrangThai = RefundState.DA_HOAN });
        await db.SaveChangesAsync();
        var service = Service(fixture, db);
        Assert.Equal(1, await service.ExpireOverdueDepositsAsync(Now, booking.Id, default));
        Assert.Equal(0, await service.ExpireOverdueDepositsAsync(Now, booking.Id, default));
        await using var verify = fixture.Database.CreateContext();
        var saved = await verify.DonDatLichSet.Include(x => x.TienCocDonDatLich).ThenInclude(x => x!.HoanCocTienCocItems).SingleAsync();
        Assert.Equal(BookingState.CANCELLED, saved.TrangThai);
        Assert.Equal(CancellationType.NO_SHOW, saved.LoaiHuy);
        Assert.Equal(Now, saved.HuyAt);
        Assert.Equal(DepositRequestState.HET_HAN, saved.TienCocDonDatLich!.TrangThaiYeuCau);
        var refund = Assert.Single(saved.TienCocDonDatLich.HoanCocTienCocItems, x => x.TrangThai == RefundState.CHO_HOAN);
        Assert.Equal(70000m, refund.SoTien);
        Assert.Equal(actors[1], refund.TaoBoiId);
        Assert.NotEqual(Guid.Empty, refund.RequestId);
        var slot = await verify.KhungGioDatLichSet.SingleAsync(x => x.Id == booking.KhungGioId);
        Assert.Equal(0, slot.SlotDaDat);
        Assert.Equal(0, slot.PhutDaGiu);
    }

    [Fact]
    public async Task Expiry_NoReceipt_CreatesNoRefund_AndHotelOnlyDoesNotReleaseSpa()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var room = await db.LoaiPhongSet.Select(x => x.Id).FirstAsync();
        var booking = await fixture.AddBookingAsync(db, false, room);
        Assert.Equal(1, await Service(fixture, db).ExpireOverdueDepositsAsync(Now, booking.Id, default));
        await using var verify = fixture.Database.CreateContext();
        Assert.Empty(await verify.HoanCocSet.ToArrayAsync());
        Assert.Equal(0, (await verify.KhungGioDatLichSet.SingleAsync(x => x.Id == booking.KhungGioId)).SlotDaDat);
    }

    [Fact]
    public async Task TargetedSweep_DrainsTwentyOneRelatedBookings_LeavesUnrelatedBooking()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var slots = await db.KhungGioDatLichSet.OrderBy(x => x.Id).ToArrayAsync();
        slots[0].TongSlot = 100;
        slots[0].TranPhut = 10000;
        await db.SaveChangesAsync();
        for (var i = 0; i < 21; i++) await fixture.AddBookingAsync(db);
        var outsideRoom = await db.LoaiPhongSet.Select(x => x.Id).FirstAsync();
        var outside = await fixture.AddBookingAsync(db, false, outsideRoom);
        outside.KhungGioId = slots[1].Id;
        await db.SaveChangesAsync();
        var observer = new BookingLockObserver();
        Assert.Equal(21, await Service(fixture, db, observer).ExpireOverdueDepositsAsync(Now, null, default, [slots[0].Id]));
        Assert.Equal(new[] { 1, 20 }, observer.BatchSizes.Order());
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(BookingState.PENDING, (await verify.DonDatLichSet.SingleAsync(x => x.Id == outside.Id)).TrangThai);
        Assert.Equal(21, await verify.DonDatLichSet.CountAsync(x => x.TrangThai == BookingState.CANCELLED));
        Assert.Equal(0, (await verify.KhungGioDatLichSet.SingleAsync(x => x.Id == slots[0].Id)).SlotDaDat);
        Assert.Equal(1, await Service(fixture, db).ExpireOverdueDepositsAsync(Now, null, default));
    }

    [Fact]
    public async Task TargetedSweep_EmptyFiltersAreNoOp_CombinedFiltersUseOr_RejectsMixedScope()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var spa = await fixture.AddBookingAsync(db);
        var roomId = await db.LoaiPhongSet.Select(x => x.Id).FirstAsync();
        await fixture.AddBookingAsync(db, false, roomId);
        var service = Service(fixture, db);
        Assert.Equal(0, await service.ExpireOverdueDepositsAsync(Now, null, default, []));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExpireOverdueDepositsAsync(Now, spa.Id, default, [spa.KhungGioId]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExpireOverdueDepositsAsync(Now, null, default, [-1]));
        Assert.Equal(2, await service.ExpireOverdueDepositsAsync(Now, null, default, [spa.KhungGioId], [roomId]));
    }

    [Fact]
    public async Task Sweep_RejectsAmbientAndCurrentTransactions_AndDoesNotCommitCallerChanges()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        var service = Service(fixture, db);
        await using (var tx = await db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpireOverdueDepositsAsync(Now, null, default));
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpireOverdueDepositsAsync(Now, null, default));
        var account = await db.NhanVienSet.FirstAsync();
        var originalName = account.TenNhanVien;
        account.TenNhanVien = "Chưa lưu";
        Assert.Equal(1, await service.ExpireOverdueDepositsAsync(Now, booking.Id, default));
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(originalName, (await verify.NhanVienSet.SingleAsync(x => x.Id == account.Id)).TenNhanVien);
        Assert.Equal("Chưa lưu", account.TenNhanVien);
    }

    [Fact]
    public async Task LockedExpiry_DoesNotCommitAndRollsBackWithCaller()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using (var seed = fixture.Database.CreateContext()) await fixture.AddBookingAsync(seed);
        await using (var db = fixture.Database.CreateContext())
        {
            var service = Service(fixture, db);
            var id = await db.DonDatLichSet.Select(x => x.Id).SingleAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpireLockedBookingDepositAsync(new DonDatLich(), Now, default));
            await using var tx = await db.Database.BeginTransactionAsync();
            var booking = await LockBookingAsync(db, id);
            await service.ExpireLockedBookingDepositAsync(booking, Now, default);
            Assert.Equal(BookingState.CANCELLED, booking.TrangThai);
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(BookingState.PENDING, (await verify.DonDatLichSet.SingleAsync()).TrangThai);
        Assert.Equal(1, (await verify.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync()).SlotDaDat);
    }

    [Fact]
    public async Task CleanupCommit_SurvivesNextWriterRollback_AndCapacityDoesNotDoubleCompensate()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        await Service(fixture, db).ExpireOverdueDepositsAsync(Now, booking.Id, default);
        await using (var tx = await db.Database.BeginTransactionAsync()) await tx.RollbackAsync();
        await using var verify = fixture.Database.CreateContext();
        var slot = await verify.KhungGioDatLichSet.Include(x => x.CaLamViec).OrderBy(x => x.Id).FirstAsync();
        var capacity = await new PetSpa.Api.Services.SpaCapacityCalculator(verify).CheckAsync([new(slot, 1, 60)], default, Now);
        Assert.Equal(10, capacity[slot.Id].SeatsLeft);
        Assert.Equal(240, capacity[slot.Id].MinutesLeft);
    }

    internal static DepositExpiryService Service(FoundationFixture fixture, PetSpaDbContext db, DbCommandInterceptor? observer = null)
    {
        var options = new DbContextOptionsBuilder<PetSpaDbContext>().UseNpgsql(fixture.Database.ConnectionString);
        if (observer is not null) options.AddInterceptors(observer);
        return new DepositExpiryService(db, options.Options, fixture.Clock);
    }

    [Fact]
    public async Task RoomTarget_DoesNotExpireBookingWhoseOnlyTargetPetWasCancelledBeforeCheckin()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var rooms = await db.LoaiPhongSet.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var booking = await fixture.AddBookingAsync(db, false, rooms[0]);
        var cancelled = booking.ChiTietDonDonDatLichItems.Single();
        cancelled.TrangThaiChamSoc = PetCareState.CANCELLED;
        cancelled.TraLaiAt = Now;
        booking.ChiTietDonDonDatLichItems.Add(new ChiTietDon
        {
            TenPetSnapshot = "Active pet", CanNangKhaiBao = 4m, LoaiPhongId = rooms[1], SoDemChot = 3,
            ThuCung = new ThuCung { TenThuCung = "Active pet", CanNang = 4m, KhachHangId = booking.KhachHangId }
        });
        booking.TienCocDonDatLich!.GiaoDichThuTienCocItems.Add(Receipt(20000m, await db.NhanVienSet.Select(x => x.Id).FirstAsync(), Now));
        await db.SaveChangesAsync();
        Assert.Equal(0, await Service(fixture, db).ExpireOverdueDepositsAsync(Now, null, default, targetRoomTypeIds: [rooms[0]]));
        await using var verify = fixture.Database.CreateContext();
        Assert.Equal(BookingState.PENDING, (await verify.DonDatLichSet.SingleAsync()).TrangThai);
        Assert.Empty(await verify.HoanCocSet.ToArrayAsync());
        Assert.Equal(1, await Service(fixture, db).ExpireOverdueDepositsAsync(Now, null, default, targetRoomTypeIds: [rooms[1]]));
    }

    internal static GiaoDichThu Receipt(decimal amount, int actor, DateTimeOffset now) => new()
    { RequestId = Guid.NewGuid(), SoTien = amount, XacNhanBoiId = actor, GiaoDichAt = now, XacNhanAt = now };

    internal static async Task<DonDatLich> LockBookingAsync(PetSpaDbContext db, int id)
    {
        await db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id={id} FOR UPDATE").ToListAsync();
        var slots = await db.DichVuTrongDonSet.Where(x => x.ChiTietDon.DonDatLichId == id).Select(x => x.KhungGioId).Distinct().ToArrayAsync();
        if (slots.Length > 0) await db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM khung_gio_dat_lich WHERE id=ANY({slots}) ORDER BY id FOR UPDATE").ToListAsync();
        var rooms = await db.ChiTietDonSet.Where(x => x.DonDatLichId == id && x.LoaiPhongId != null).Select(x => x.LoaiPhongId!.Value).Distinct().ToArrayAsync();
        await new PetSpa.Api.Services.HotelCapacityCalculator(db).LockRoomTypesAsync(rooms, default);
        await db.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM tien_coc WHERE don_dat_lich_id={id} FOR UPDATE").ToListAsync();
        return await db.DonDatLichSet.Include(x => x.TienCocDonDatLich).ThenInclude(x => x!.GiaoDichThuTienCocItems)
            .Include(x => x.TienCocDonDatLich).ThenInclude(x => x!.HoanCocTienCocItems)
            .Include(x => x.ChiTietDonDonDatLichItems).ThenInclude(x => x.DichVuTrongDonChiTietDonItems).ThenInclude(x => x.KhungGio)
            .SingleAsync(x => x.Id == id);
    }

    private sealed class BookingLockObserver : DbCommandInterceptor
    {
        public ConcurrentBag<int> BatchSizes { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM don_dat_lich") && command.CommandText.Contains("FOR UPDATE"))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is int[] ids) BatchSizes.Add(ids.Length);
            return ValueTask.FromResult(result);
        }
    }
}
