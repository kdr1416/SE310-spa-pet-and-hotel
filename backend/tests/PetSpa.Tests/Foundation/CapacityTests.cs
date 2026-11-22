using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Services;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Enums;

namespace PetSpa.Tests.Foundation;

public sealed class CapacityTests
{
    private static readonly DateTimeOffset Now = BookingDepositProjectionTests.Now;
    private static readonly DateOnly Day = new(2035, 1, 1);

    [Theory]
    [InlineData(PetCareState.DONE, true, false, BookingState.IN_PROGRESS, 4, 4, 4)]
    [InlineData(PetCareState.CANCELLED, true, true, BookingState.IN_PROGRESS, 4, 5, 5)]
    [InlineData(PetCareState.CANCELLED, true, false, BookingState.IN_PROGRESS, 4, 4, 4)]
    [InlineData(PetCareState.CANCELLED, false, false, BookingState.IN_PROGRESS, 5, 5, 5)]
    [InlineData(PetCareState.DONE, true, false, BookingState.COMPLETED, 5, 5, 5)]
    [InlineData(PetCareState.CANCELLED, true, true, BookingState.CANCELLED, 5, 5, 5)]
    public async Task Hotel_PreservesConsumedAndCommittedNights_AndOnlyReleasesEligibleNights(
        PetCareState petState, bool checkedIn, bool returned, BookingState state, int first, int second, int third)
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var room = await db.LoaiPhongSet.SingleAsync(x => x.TenPhong == "Phòng Tiêu Chuẩn");
        var booking = await fixture.AddBookingAsync(db, false, room.Id, false);
        booking.TrangThai = state;
        var pet = booking.ChiTietDonDonDatLichItems.Single();
        pet.TrangThaiChamSoc = petState;
        pet.CheckInAt = checkedIn ? Now : null;
        pet.TraLaiAt = returned ? Now.AddDays(1) : null;
        pet.SoDemDaO = 1;
        await db.SaveChangesAsync();
        var calculator = new HotelCapacityCalculator(db);
        var result = await calculator.CheckRoomAvailabilityAsync(Day, Day.AddDays(3), [room.Id], null, Now, default);
        Assert.Equal(new[] { first, second, third }, result.Select(x => x.SuatConLai));
        Assert.Equal(new[] { Day, Day.AddDays(1), Day.AddDays(2) }, result.Select(x => x.Ngay));
        var excluded = await calculator.CheckRoomAvailabilityAsync(Day, Day.AddDays(3), [room.Id], booking.Id, Now, default);
        Assert.All(excluded, x => Assert.Equal(5, x.SuatConLai));
    }

    [Theory]
    [InlineData(-1, DepositRequestState.CHO_THU, 4)]
    [InlineData(0, DepositRequestState.CHO_THU, 5)]
    [InlineData(1, DepositRequestState.CHO_THU, 5)]
    [InlineData(1, DepositRequestState.DA_DAP_UNG, 4)]
    public async Task Hotel_ExpiredHold_BeforeAtAfterDeadline_IsExcludedOnlyWhenDue(int ticks,
        DepositRequestState state, int expected)
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var roomId = await db.LoaiPhongSet.Where(x => x.TenPhong == "Phòng Tiêu Chuẩn").Select(x => x.Id).SingleAsync();
        var booking = await fixture.AddBookingAsync(db, true, roomId);
        booking.TienCocDonDatLich!.TrangThaiYeuCau = state;
        await db.SaveChangesAsync();
        var result = await new HotelCapacityCalculator(db).CheckRoomAvailabilityAsync(Day, Day.AddDays(3), [roomId, roomId], null, Now.AddTicks(ticks), default);
        Assert.Equal(3, result.Count);
        Assert.All(result, x => Assert.Equal(expected, x.SuatConLai));
        await db.Entry(booking).ReloadAsync();
        Assert.Equal(BookingState.PENDING, booking.TrangThai);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Hotel_CountsDistinctPetsPerNight_DespiteMultipleServiceRows()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var roomId = await db.LoaiPhongSet.Where(x => x.TenPhong == "Phòng Tiêu Chuẩn").Select(x => x.Id).SingleAsync();
        var booking = await fixture.AddBookingAsync(db, true, roomId, false);
        var pet = booking.ChiTietDonDonDatLichItems.Single();
        var line = pet.DichVuTrongDonChiTietDonItems.Single();
        pet.DichVuTrongDonChiTietDonItems.Add(new DichVuTrongDon
        {
            RequestId = Guid.NewGuid(), DichVuId = line.DichVuId, KhungGioId = line.KhungGioId,
            TenSnapshot = "Dịch vụ thêm", ThoiLuongSnapshot = 30
        });
        await db.SaveChangesAsync();
        var result = await new HotelCapacityCalculator(db).CheckRoomAvailabilityAsync(Day, Day.AddDays(3), [roomId], null, Now, default);
        Assert.All(result, x => Assert.Equal(4, x.SuatConLai));
    }

    [Fact]
    public async Task Spa_ReadCompensation_CountsPetOnceAndComboMinutesOnce_WithoutWriting()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        var line = booking.ChiTietDonDonDatLichItems.Single().DichVuTrongDonChiTietDonItems.Single();
        var serviceIds = await db.DichVuSet.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        line.DichVuId = null;
        line.ComboId = await db.ComboSet.Select(x => x.Id).SingleAsync();
        line.ThoiLuongSnapshot = 150;
        line.ThanhPhanDichVuTrongDonDichVuTrongDonItems = [
            new ThanhPhanDichVuTrongDon { DichVuId = serviceIds[0], TenSnapshot = "Tắm", ThoiLuongSnapshot = 60 },
            new ThanhPhanDichVuTrongDon { DichVuId = serviceIds[1], TenSnapshot = "Cắt", ThoiLuongSnapshot = 90 }];
        var slot = await db.KhungGioDatLichSet.Include(x => x.CaLamViec).SingleAsync(x => x.Id == line.KhungGioId);
        slot.PhutDaGiu = 150;
        var roomId = await db.LoaiPhongSet.Select(x => x.Id).FirstAsync();
        await fixture.AddBookingAsync(db, false, roomId); // Hotel-only never contributes a Spa seat.
        await db.SaveChangesAsync();
        var calculator = new SpaCapacityCalculator(db);
        var demand = new SpaSlotDemand(slot, 1, 150);
        var writer = (await calculator.CheckAsync([demand], default))[slot.Id];
        var read = (await calculator.CheckAsync([demand], default, readAsOfNow: Now))[slot.Id];
        Assert.Equal(9, writer.SeatsLeft);
        Assert.Equal(90, writer.MinutesLeft);
        Assert.Equal(10, read.SeatsLeft);
        Assert.Equal(240, read.MinutesLeft);
        await db.Entry(slot).ReloadAsync();
        Assert.Equal(1, slot.SlotDaDat);
        Assert.Equal(150, slot.PhutDaGiu);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Hotel_RejectsInvalidDates_AndRequiresTransactionForOrderedRoomLocks()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var calculator = new HotelCapacityCalculator(db);
        await Assert.ThrowsAsync<ArgumentException>(() => calculator.CheckRoomAvailabilityAsync(Day, Day, [1], null, Now, default));
        Assert.Empty(await calculator.CheckRoomAvailabilityAsync(Day, Day.AddDays(1), [], null, Now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => calculator.LockRoomTypesAsync([1], default));
        await using var tx = await db.Database.BeginTransactionAsync();
        var ids = await db.LoaiPhongSet.Select(x => x.Id).ToArrayAsync();
        await calculator.LockRoomTypesAsync([ids[1], ids[0], ids[1]], default);
        await tx.RollbackAsync();
    }
}
