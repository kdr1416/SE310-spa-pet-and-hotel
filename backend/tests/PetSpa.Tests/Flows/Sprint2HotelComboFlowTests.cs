using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.DTOs.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Care;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Flows;

public sealed class Sprint2HotelComboFlowTests
{
    private static readonly DateOnly ClinicDate = new(2026, 11, 20);
    private static readonly DateTimeOffset BookingTime = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly Contact Contact = new("Khách Hotel Combo", "0912345678", "hotel@example.test");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    };

    [Fact]
    public async Task HotelCombo_FullLifecycle_WithDeposit_ExtraServices_InvoiceRevoke_AndEarlyHandover_Passes()
    {
        await using var f = await Flow.CreateAsync();
        await f.ScheduleAsync(); // E54, E28, E29, E27; real registration and approval, no seeded shifts for today.
        var pets = new List<PetSelection>
        {
            f.Pet("Milo", f.Morning, hotel: true, combo: true),
            f.Pet("Nâu", f.Morning, hotel: false, combo: false)
        };
        var capacity = await ReadAsync<AvailabilityResponse>(await f.Customer.PostAsJsonAsync("/api/availability",
            new AvailabilityQuery
            {
                MaKhungGio = f.Morning, NgayTra = ClinicDate.AddDays(2),
                ThuCung = pets.Select(p => new PetDemand { MaLoaiPhong = p.MaLoaiPhong, DichVu = p.DichVu }).ToList()
            })); // E03
        Assert.True(capacity.CoTheDat);

        var booking = await f.BookAsync(f.Customer, "/api/bookings", pets); // E04
        // Independently calculated from standard seed prices: 2 * 250k room + 350k combo + 150k Spa.
        Assert.Equal("PENDING", booking.TrangThai);
        Assert.Equal(1_000_000m, booking.TongTamTinh);
        AssertDeposit(booking, 1_000_000m, 300_000m); // M2-06 includes the Spa-only pet.
        var milo = Assert.Single(booking.ThuCung, p => p.TenThuCung == "Milo");
        var nau = Assert.Single(booking.ThuCung, p => p.TenThuCung == "Nâu");
        Assert.Null(nau.MaLoaiPhong);
        await f.AssertStateAsync(booking.MaDon, BookingState.PENDING);

        var deposits = await ReadAsync<PageDeposit>(await f.Reception.GetAsync("/api/internal/deposits")); // E43
        Assert.Contains(deposits.Items, d => d.MaTienCoc == booking.TienCoc!.MaTienCoc);
        await f.ReceiveDepositAsync(booking, 300_000m);
        var detail = await f.DetailAsync(booking.MaDon); // E10b
        Assert.Equal("DA_DAP_UNG", detail.TienCoc!.TrangThaiYeuCau);
        await f.ConfirmAsync(booking.MaDon); // E11
        await f.AssertStateAsync(booking.MaDon, BookingState.CONFIRMED);

        var customer = Assert.Single(await ReadAsync<List<CustomerDto>>(await f.Reception.GetAsync(
            $"/api/internal/customers/lookup?soDienThoai={Contact.SoDienThoai}"))); // E06
        Assert.Contains(customer.ThuCung, p => p.MaThuCung == milo.MaThuCung);
        var edited = await ReadAsync<PetDto>(await f.Reception.PatchAsJsonAsync($"/api/internal/pets/{milo.MaThuCung}",
            new { requestId = Guid.NewGuid(), ghiChuDiUng = "Nhạy cảm với sữa tắm có hương liệu" })); // E07
        Assert.Equal("Nhạy cảm với sữa tắm có hương liệu", edited.GhiChuDiUng);
        var candidates = await ReadAsync<List<Employee>>(await f.Reception.GetAsync(
            $"/api/internal/assignment-candidates?ngay={ClinicDate:yyyy-MM-dd}&maCa={f.MorningShift}")); // E14
        Assert.Contains(candidates, e => e.MaNhanVien == f.TechnicianId);

        f.Clock.UtcNow = BookingTime.AddMinutes(90); // 08:30 clinic time, after confirmation and during morning shift.
        // Assign to Admin first, then actually transfer ownership to the technician through E16.
        var checkedIn = await f.CheckinAsync(milo.MaChiTietDon, f.AdminId, "ROI_NANG"); // E15
        Assert.Contains("ROI_NANG", checkedIn.TinhTrangLong!);
        await ReadAsync<BookingPet>(await f.Reception.PutAsJsonAsync($"/api/internal/booking-pets/{milo.MaChiTietDon}/assignment",
            new { requestId = Guid.NewGuid(), maNhanVien = f.TechnicianId, lyDo = "Chuyển kỹ thuật viên ca sáng" })); // E16
        await f.CheckinAsync(nau.MaChiTietDon, f.TechnicianId);
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS);
        await using (var db = f.Database.CreateContext())
        {
            Assert.All(await db.ChiTietDonSet.Where(p => p.DonDatLichId == booking.MaDon).ToListAsync(), p =>
            {
                Assert.Equal(f.TechnicianId, p.NhanVienId);
                Assert.NotNull(p.CheckInAt);
                Assert.Equal(PetCareState.WAITING, p.TrangThaiChamSoc);
            });
        }

        await f.CareAsync(milo.MaChiTietDon, "START"); // E17
        await f.CareAsync(nau.MaChiTietDon, "START");
        var combo = Assert.Single(checkedIn.DichVu);
        Assert.Equal(2, combo.ThanhPhan.Count);
        var partial = await f.CompleteAsync(combo, combo.ThanhPhan[0].MaThanhPhan); // E61
        Assert.Equal("CHO_THUC_HIEN", partial.TrangThai);
        using (var form = Form(new { requestId = Guid.NewGuid(), hanhDong = "READY" }, "anhThanhPham"))
            await ErrorAsync(await f.Technician.PostAsync($"/api/internal/booking-pets/{milo.MaChiTietDon}/care-actions", form),
                HttpStatusCode.Conflict, "INVALID_TRANSITION");
        await using (var db = f.Database.CreateContext())
        {
            Assert.Equal(PetCareState.IN_PROGRESS, (await db.ChiTietDonSet.SingleAsync(p => p.Id == milo.MaChiTietDon)).TrangThaiChamSoc);
            Assert.Equal(1, await db.ThanhPhanDichVuTrongDonSet.CountAsync(c => c.DichVuTrongDonId == combo.MaDichVuTrongDon && c.TrangThai == OrderServiceState.HOAN_TAT));
        }
        Assert.Equal("HOAN_TAT", (await f.CompleteAsync(combo, combo.ThanhPhan[1].MaThanhPhan)).TrangThai);

        var extra = await ReadAsync<OrderService>(await f.Reception.PostAsJsonAsync(
            $"/api/internal/booking-pets/{nau.MaChiTietDon}/extra-services",
            new { requestId = Guid.NewGuid(), maDichVu = f.Ears, maKhungGio = f.Morning, lyDo = "Khách yêu cầu vệ sinh tai" }),
            HttpStatusCode.Created); // E20; 50k, making exactly 240 reserved minutes.
        Assert.Equal(50_000m, extra.GiaChot);
        Assert.Equal("HOAN_TAT", (await ReadAsync<OrderService>(await f.Technician.PostAsJsonAsync(
            $"/api/internal/extra-services/{extra.MaDichVuTrongDon}/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "COMPLETE" }))).TrangThai); // E21
        foreach (var pet in new[] { milo, nau })
        {
            var ready = await f.CareAsync(pet.MaChiTietDon, "READY");
            Assert.Equal("READY", ready.TrangThai);
            Assert.StartsWith("https://photo.test/sign/", Assert.Single(ready.NhatKy, l => l.LoaiNhatKy == "CARE_READY").AnhUrl);
        }
        AssertDeposit(await f.DetailAsync(booking.MaDon), 1_000_000m, 300_000m); // Extra work never changes the deposit basis.

        var early = await ReadAsync<BookingPet>(await f.Admin.PostAsJsonAsync(
            $"/api/admin/booking-pets/{nau.MaChiTietDon}/early-handover", Reason("Khách nhận pet Spa trước"))); // E48
        Assert.Equal("DONE", early.TrangThai);
        Assert.True(early.NhanSom);
        Assert.NotNull(early.BanGiaoAt);
        await using (var db = f.Database.CreateContext())
        {
            var saved = await db.ChiTietDonSet.SingleAsync(p => p.Id == nau.MaChiTietDon);
            Assert.True(saved.NhanSom);
            Assert.Equal(PetCareState.DONE, saved.TrangThaiChamSoc);
        }
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS);
        await ErrorAsync(await f.Admin.PostAsJsonAsync($"/api/admin/booking-pets/{milo.MaChiTietDon}/early-handover",
            Reason("Không còn pet bảo đảm tại tiệm")), HttpStatusCode.Conflict, "INVALID_TRANSITION");

        var first = await f.InvoiceAsync(booking.MaDon); // E23
        Assert.Equal("UNPAID", first.TrangThai);
        Assert.Equal(1_050_000m, first.TongTien);
        Assert.Equal(300_000m, first.CocKhauTru);
        Assert.Equal(750_000m, first.ConPhaiTra);
        var roomLine = Assert.Single(first.DongTien, l => l.LoaiKhoan == "HOTEL");
        Assert.Equal((milo.MaChiTietDon, 2, 250_000m, 500_000m),
            (roomLine.MaChiTietDon!.Value, roomLine.SoLuong, roomLine.DonGia, roomLine.ThanhTien));
        Assert.Equal(350_000m, Assert.Single(first.DongTien, l => l.MaDichVuTrongDon == combo.MaDichVuTrongDon).ThanhTien);
        Assert.Equal(4, first.DongTien.Count);
        var revoked = await ReadAsync<InvoiceResponseDto>(await f.Admin.PostAsJsonAsync(
            $"/api/internal/invoices/{first.MaHoaDon}/withdraw", Reason("Thiếu phụ phí gỡ rối"))); // E25
        Assert.Equal("REVOKED", revoked.TrangThai);
        Assert.NotNull(revoked.ThuHoiAt);
        Assert.Equal(first.DongTien.ToArray(), revoked.DongTien.ToArray());
        Assert.Equal(first.CocKhauTru, revoked.CocKhauTru);

        var fee = await f.FeeAsync(booking.MaDon, milo.MaChiTietDon, "Phụ phí gỡ rối", 100_000m); // E22a
        var amended = await ReadAsync<FeeDto>(await f.Reception.PatchAsJsonAsync($"/api/internal/fees/{fee.MaPhuPhi}",
            new { requestId = Guid.NewGuid(), soTien = 80_000m, lyDo = "Chốt theo mức độ rối thực tế" })); // E22b
        Assert.Equal(80_000m, amended.SoTien);
        // E22c: a mistaken common fee must disappear from the replacement invoice.
        var mistaken = await f.FeeAsync(booking.MaDon, null, "Phí nhập nhầm", 20_000m);
        var cancelledFee = await ReadAsync<FeeDto>(await f.Reception.PostAsJsonAsync(
            $"/api/internal/fees/{mistaken.MaPhuPhi}/cancel", Reason("Nhập nhầm khoản phí")));
        Assert.NotNull(cancelledFee.HuyAt);

        var replacement = await f.InvoiceAsync(booking.MaDon);
        Assert.NotEqual(first.MaHoaDon, replacement.MaHoaDon);
        Assert.Equal(first.MaHoaDon, replacement.ThayTheHoaDon);
        Assert.Equal(1_130_000m, replacement.TongTien);
        Assert.Equal(300_000m, replacement.CocKhauTru);
        Assert.Equal(830_000m, replacement.ConPhaiTra);
        Assert.DoesNotContain(replacement.DongTien, l => l.MaPhuPhi == mistaken.MaPhuPhi);
        var feeLine = Assert.Single(replacement.DongTien, l => l.MaPhuPhi == fee.MaPhuPhi);
        Assert.Equal(milo.MaChiTietDon, feeLine.MaChiTietDon);
        Assert.Equal("Milo", feeLine.TenPet);
        var paid = await ReadAsync<InvoiceResponseDto>(await f.Reception.PostAsJsonAsync(
            $"/api/internal/invoices/{replacement.MaHoaDon}/checkout",
            new { requestId = Guid.NewGuid(), phuongThuc = "CASH", soTienThucNhan = 830_000m, soTienDaTraLai = 0m })); // E26
        Assert.Equal("PAID", paid.TrangThai);
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS); // Payment does not hand over Milo.
        await ErrorAsync(await f.Admin.PostAsJsonAsync($"/api/internal/invoices/{paid.MaHoaDon}/withdraw",
            Reason("Không được thu hồi hóa đơn đã thu tiền")), HttpStatusCode.Conflict, "INVALID_TRANSITION");
        var done = await ReadAsync<BookingPet>(await f.Reception.PostAsJsonAsync(
            $"/api/internal/booking-pets/{milo.MaChiTietDon}/handover",
            new { requestId = Guid.NewGuid(), xacNhanDaNhan = true })); // E19
        Assert.Equal("DONE", done.TrangThai);
        await f.AssertStateAsync(booking.MaDon, BookingState.COMPLETED);
        await using var final = f.Database.CreateContext();
        var invoices = await final.HoaDonSet.Include(i => i.CtHoaDonHoaDonItems).Where(i => i.DonDatLichId == booking.MaDon).ToListAsync();
        Assert.Equal(2, invoices.Count);
        Assert.Equal(InvoiceState.PAID, Assert.Single(invoices, i => i.TrangThai != InvoiceState.REVOKED).TrangThai);
        Assert.Equal(4, Assert.Single(invoices, i => i.Id == first.MaHoaDon).CtHoaDonHoaDonItems.Count);
        Assert.Equal(830_000m, (await final.GiaoDichThuSet.SingleAsync(r => r.HoaDonId == replacement.MaHoaDon)).SoTien);
        Assert.Equal(300_000m, (await f.DetailAsync(booking.MaDon)).TienCoc!.KhauTru);
        // M2-L08: financial history prevents deleting its booking or catalog references in PostgreSQL.
        await AssertRestrictedDeleteAsync(f, "DELETE FROM don_dat_lich WHERE id = {0}", booking.MaDon);
        await AssertRestrictedDeleteAsync(f, "DELETE FROM combo WHERE id = {0}", f.Combo);
    }

    [Fact]
    public async Task CounterBooking_SinglePetCancel_DepositSurplus_RefundFlow_AndAllCancelledClose_Passes()
    {
        await using var f = await Flow.CreateAsync();
        await f.ScheduleAsync();
        // Separate slots: two 150-minute combos cannot fit the seeded 240-minute morning shift.
        var booking = await f.BookAsync(f.Reception, "/api/internal/bookings",
            [f.Pet("Milo", f.Morning, true, true), f.Pet("Nâu", f.Afternoon, true, true)]); // E05
        Assert.Equal(1_700_000m, booking.TongTamTinh);
        AssertDeposit(booking, 1_700_000m, 510_000m);
        await f.ReceiveDepositAsync(booking, 510_000m);
        await f.ConfirmAsync(booking.MaDon);
        var milo = Assert.Single(booking.ThuCung, p => p.TenThuCung == "Milo");
        var nau = Assert.Single(booking.ThuCung, p => p.TenThuCung == "Nâu");
        f.Clock.UtcNow = BookingTime.AddMinutes(90);
        var checkedIn = await f.CheckinAsync(milo.MaChiTietDon, f.TechnicianId);
        await f.CareAsync(milo.MaChiTietDon, "START");
        var combo = Assert.Single(checkedIn.DichVu);
        var bath = Assert.Single(combo.ThanhPhan, c => c.MaDichVu == f.Bath);
        await f.CompleteAsync(combo, bath.MaThanhPhan); // E61, just one component.

        var notArrived = await f.CancelAsync(nau.MaChiTietDon, 0, []); // E47
        Assert.Equal("CANCELLED", notArrived.TrangThai);
        Assert.NotNull(notArrived.TraLaiAt);
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS);
        f.Clock.UtcNow = BookingTime.AddDays(1).AddMinutes(90); // One real night, no sleep or manipulated check-in timestamp.
        var cancelled = await f.CancelAsync(milo.MaChiTietDon, 1, [bath.MaThanhPhan]);
        Assert.Equal("CANCELLED", cancelled.TrangThai);
        Assert.Null(cancelled.TraLaiAt); // Still physically in the clinic.
        var detail = await f.DetailAsync(booking.MaDon);
        Assert.Equal(400_000m, detail.TongTamTinh); // 1 * 250k room + retail bath 150k, not the full 350k combo.
        Assert.Equal(510_000m, detail.TienCoc!.ThucNhan);
        Assert.Equal(0m, detail.TienCoc.GiuLai);
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS);
        await using (var db = f.Database.CreateContext())
        {
            var pet = await db.ChiTietDonSet.SingleAsync(p => p.Id == milo.MaChiTietDon);
            Assert.Equal(1, pet.SoDemDaO);
            var line = await db.DichVuTrongDonSet.Include(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                .SingleAsync(s => s.Id == combo.MaDichVuTrongDon);
            Assert.Equal(150_000m, line.GiaChot);
            Assert.Equal(OrderServiceState.HUY, Assert.Single(line.ThanhPhanDichVuTrongDonDichVuTrongDonItems, c => c.Id != bath.MaThanhPhan).TrangThai);
            Assert.Equal(0, (await db.KhungGioDatLichSet.SingleAsync(s => s.Id == f.Afternoon)).SlotDaDat);
        }

        var depositId = detail.TienCoc.MaTienCoc;
        var refund = await ReadAsync<RefundDto>(await f.Admin.PostAsJsonAsync($"/api/admin/deposits/{depositId}/refunds",
            new { requestId = Guid.NewGuid(), soTien = 110_000m, lyDo = "Hoàn cọc dư sau hủy riêng" }), HttpStatusCode.Created); // E44
        Assert.Equal("CHO_HOAN", refund.TrangThai);
        Assert.Equal(110_000m, refund.SoTien);
        await ErrorAsync(await f.Admin.PostAsJsonAsync($"/api/admin/deposits/{depositId}/refunds",
            new { requestId = Guid.NewGuid(), soTien = 1m, lyDo = "Không được dành hoàn hai lần" }),
            HttpStatusCode.Conflict, "AMOUNT_MISMATCH");
        var refunds = await ReadAsync<PageRefund>(await f.Admin.GetAsync("/api/admin/refunds")); // E46
        Assert.Equal(refund.MaHoanCoc, Assert.Single(refunds.Items).MaHoanCoc);
        var pending = await f.TrackAsync(booking);
        Assert.Equal("CHO_HOAN", Assert.Single(pending.TienCoc!.HoanCoc).TrangThai);
        var confirmed = await PostFormAsync<RefundDto>(f.Admin, $"/api/admin/refunds/{refund.MaHoanCoc}/confirm",
            new { requestId = Guid.NewGuid(), phuongThuc = "BANK_TRANSFER", hoanAt = f.Clock.UtcNow, thamChieu = "TEST-REFUND-27" }); // E45
        Assert.Equal("DA_HOAN", confirmed.TrangThai);
        Assert.Equal(f.Clock.UtcNow, confirmed.HoanAt);
        var refunded = await f.TrackAsync(booking);
        Assert.Equal("DA_HOAN", Assert.Single(refunded.TienCoc!.HoanCoc).TrangThai);
        Assert.Equal(110_000m, refunded.TienCoc.DaHoan);
        // DanhHoan includes every allocated refund, including DA_HOAN; DaHoan is its paid subset.
        Assert.Equal(110_000m, refunded.TienCoc.DanhHoan);

        var invoice = await f.InvoiceAsync(booking.MaDon);
        Assert.Equal(400_000m, invoice.TongTien);
        Assert.Equal(400_000m, invoice.CocKhauTru);
        Assert.Equal(0m, invoice.ConPhaiTra);
        Assert.Equal("PAID", invoice.TrangThai);
        Assert.NotNull(invoice.PaidAt);
        Assert.Equal(2, invoice.DongTien.Count);
        Assert.All(invoice.DongTien, l => Assert.Equal(milo.MaChiTietDon, l.MaChiTietDon));
        await f.AssertStateAsync(booking.MaDon, BookingState.IN_PROGRESS);
        var returned = await ReadAsync<BookingPet>(await f.Reception.PostAsJsonAsync(
            $"/api/internal/booking-pets/{milo.MaChiTietDon}/return-cancelled", Command())); // E49
        Assert.Equal("CANCELLED", returned.TrangThai);
        Assert.Equal(f.Clock.UtcNow, returned.TraLaiAt);
        await f.AssertStateAsync(booking.MaDon, BookingState.CANCELLED); // M2-05
        await using var final = f.Database.CreateContext();
        var savedPets = await final.ChiTietDonSet.Where(p => p.DonDatLichId == booking.MaDon).ToListAsync();
        Assert.All(savedPets, p => { Assert.Equal(PetCareState.CANCELLED, p.TrangThaiChamSoc); Assert.NotNull(p.TraLaiAt); });
        Assert.Equal(RefundState.DA_HOAN, (await final.HoanCocSet.SingleAsync()).TrangThai);
        Assert.Equal(510_000m, await final.GiaoDichThuSet.Where(r => r.TienCocId == depositId).SumAsync(r => r.SoTien));
        Assert.False(await final.GiaoDichThuSet.AnyAsync(r => r.HoaDonId == invoice.MaHoaDon));
        var end = await f.DetailAsync(booking.MaDon);
        Assert.Equal(510_000m, end.TienCoc!.KhauTru + end.TienCoc.DaHoan);
    }

    [Fact]
    public async Task HotelOnly_RejectsEmptyDemand_ReservesNoSpaCapacity_AndInvoicesRoomAndCommonFee()
    {
        await using var f = await Flow.CreateAsync();
        await f.ScheduleAsync();
        var empty = new BookingCreate(Guid.NewGuid(), Contact, f.Morning, ClinicDate.AddDays(2), []);
        using (var form = Form(empty))
            await ErrorAsync(await f.Customer.PostAsync("/api/bookings", form), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var noDemand = new PetSelection(4m, [], TenThuCung: "Không dịch vụ", LoaiPet: "CHO");
        using (var form = Form(empty with { RequestId = Guid.NewGuid(), ThuCung = [noDemand] }))
            await ErrorAsync(await f.Customer.PostAsync("/api/bookings", form), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await using (var db = f.Database.CreateContext())
        {
            Assert.False(await db.DonDatLichSet.AnyAsync()); // M2-L02: validation cannot leave orphan headers/pets.
            Assert.False(await db.ChiTietDonSet.AnyAsync());
        }

        var hotel = noDemand with { TenThuCung = "Hotel only", MaLoaiPhong = f.Room };
        var booking = await f.BookAsync(f.Customer, "/api/bookings", [hotel]);
        Assert.Equal(500_000m, booking.TongTamTinh);
        AssertDeposit(booking, 500_000m, 150_000m);
        await using (var db = f.Database.CreateContext())
        {
            Assert.False(await db.DichVuTrongDonSet.AnyAsync());
            var slot = await db.KhungGioDatLichSet.SingleAsync(s => s.Id == f.Morning);
            Assert.Equal((0, 0), (slot.SlotDaDat, slot.PhutDaGiu)); // M2-L01
        }
        await f.ReceiveDepositAsync(booking, 150_000m);
        await f.ConfirmAsync(booking.MaDon);
        f.Clock.UtcNow = BookingTime.AddMinutes(90);
        var pet = Assert.Single(booking.ThuCung);
        await f.CheckinAsync(pet.MaChiTietDon, f.TechnicianId);
        await f.CareAsync(pet.MaChiTietDon, "START");
        await f.CareAsync(pet.MaChiTietDon, "READY");
        var other = await f.BookAsync(f.Reception, "/api/internal/bookings", [hotel with { TenThuCung = "Pet đơn khác" }]);
        var otherPet = Assert.Single(other.ThuCung);
        // M2-L09: a pet from another booking must never appear on this invoice through an injected fee.
        await ErrorAsync(await f.Reception.PostAsJsonAsync($"/api/internal/bookings/{booking.MaDon}/fees", new
        {
            requestId = Guid.NewGuid(), maChiTietDon = otherPet.MaChiTietDon,
            tenPhuPhi = "Phí pet sai đơn", soTien = 10_000m, lyDo = "Kiểm tra liên kết đơn"
        }), HttpStatusCode.NotFound, "NOT_FOUND");
        var common = await f.FeeAsync(booking.MaDon, null, "Phí chung", 20_000m);
        // M2-L03: force a real PostgreSQL line-write failure, then prove the whole invoice transaction rolled back.
        await using (var db = f.Database.CreateContext())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_test_invoice_line() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Injected invoice line failure'; END $$;
                CREATE TRIGGER reject_test_invoice_line BEFORE INSERT ON ct_hoa_don
                FOR EACH ROW EXECUTE FUNCTION reject_test_invoice_line();
                """);
        try
        {
            await ErrorAsync(await f.Reception.PostAsJsonAsync($"/api/internal/bookings/{booking.MaDon}/invoices", Command()),
                HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR");
            await using var db = f.Database.CreateContext();
            Assert.False(await db.HoaDonSet.AnyAsync());
            Assert.False(await db.CtHoaDonSet.AnyAsync());
            Assert.False(await db.AuditLogSet.AnyAsync(a => a.HanhDong == "INVOICE_CREATE"));
        }
        finally
        {
            await using var db = f.Database.CreateContext();
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_test_invoice_line ON ct_hoa_don;
                DROP FUNCTION reject_test_invoice_line();
                """);
        }
        var invoice = await f.InvoiceAsync(booking.MaDon);
        Assert.Equal(520_000m, invoice.TongTien);
        Assert.Equal(150_000m, invoice.CocKhauTru);
        Assert.Equal(370_000m, invoice.ConPhaiTra);
        var room = Assert.Single(invoice.DongTien, l => l.LoaiKhoan == "HOTEL");
        Assert.Equal((pet.MaChiTietDon, 2, 250_000m, 500_000m),
            (room.MaChiTietDon!.Value, room.SoLuong, room.DonGia, room.ThanhTien));
        var fee = Assert.Single(invoice.DongTien, l => l.MaPhuPhi == common.MaPhuPhi);
        Assert.Null(fee.MaChiTietDon);
        Assert.Null(fee.TenPet);
        Assert.Contains("Phí chung đơn", fee.DienGiai);
        await using var final = f.Database.CreateContext();
        Assert.Equal(2, await final.CtHoaDonSet.CountAsync(l => l.HoaDonId == invoice.MaHoaDon)); // M2-L03
        Assert.False(await final.PhuPhiSet.AnyAsync(p => p.ChiTietDonId == otherPet.MaChiTietDon));
    }

    [Fact]
    public async Task Deposit_PartialReceiptsAndReplay_ThenDeadlineExpiry_RefundsAllMoneyWithoutRevivingCapacity()
    {
        await using var f = await Flow.CreateAsync();
        await f.ScheduleAsync();
        var booking = await f.BookAsync(f.Customer, "/api/bookings", [f.Pet("Milo", f.Morning, true, true)]);
        var depositId = booking.TienCoc!.MaTienCoc;
        var route = $"/api/internal/deposits/{depositId}/receipts";
        var first = new
        {
            requestId = Guid.NewGuid(), soTien = 100_000m, phuongThuc = "BANK_TRANSFER", giaoDichAt = f.Clock.UtcNow,
            maGiaoDich = "TEST-BANK-27", nganHang = "TEST"
        };
        var partial = await ReadAsync<DepositDto>(await f.Reception.PostAsJsonAsync(route, first), HttpStatusCode.Created);
        Assert.Equal("CHO_THU", partial.TrangThaiYeuCau);
        Assert.Equal(100_000m, partial.ThucNhan);
        Assert.Equal(BookingTime.AddMinutes(30), partial.HetHanAt);
        var replay = await ReadAsync<DepositDto>(await f.Reception.PostAsJsonAsync(route, first)); // Same requestId on an intentional retry.
        Assert.Equal(100_000m, replay.ThucNhan); // M2-L05
        await using (var db = f.Database.CreateContext())
            Assert.Equal(1, await db.GiaoDichThuSet.CountAsync(r => r.TienCocId == depositId));

        f.Clock.UtcNow = BookingTime.AddMinutes(30); // Exact deadline: expiry wins under the booking lock, M2-L06.
        var late = await ReadAsync<DepositDto>(await f.Reception.PostAsJsonAsync(route, new
        {
            requestId = Guid.NewGuid(), soTien = 155_000m, phuongThuc = "CASH", giaoDichAt = f.Clock.UtcNow
        }), HttpStatusCode.Created);
        Assert.Equal("HET_HAN", late.TrangThaiYeuCau);
        Assert.Equal(255_000m, late.ThucNhan);
        Assert.Equal(255_000m, late.DanhHoan);
        Assert.Equal(0m, late.KhauTru);
        Assert.Equal(0m, late.GiuLai);
        await f.AssertStateAsync(booking.MaDon, BookingState.CANCELLED);
        await using var final = f.Database.CreateContext();
        var slot = await final.KhungGioDatLichSet.SingleAsync(s => s.Id == f.Morning);
        Assert.Equal((0, 0), (slot.SlotDaDat, slot.PhutDaGiu));
        Assert.Equal(2, await final.GiaoDichThuSet.CountAsync(r => r.TienCocId == depositId));
        Assert.Equal(255_000m, await final.HoanCocSet.Where(r => r.TienCocId == depositId).SumAsync(r => r.SoTien));
    }

    private static async Task AssertRestrictedDeleteAsync(Flow f, string sql, int id)
    {
        await using var db = f.Database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, id));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await transaction.RollbackAsync();
    }

    private static void AssertDeposit(BookingResponse booking, decimal basis, decimal amount)
    {
        Assert.NotNull(booking.TienCoc);
        Assert.Equal(basis, booking.TienCoc.CoSoTinh);
        Assert.Equal(0.30m, booking.TienCoc.TyLe);
        Assert.Equal(amount, booking.TienCoc.SoTienYeuCau);
        Assert.Equal(BookingTime.AddMinutes(30), booking.TienCoc.HetHanAt);
    }

    private static object Command() => new { requestId = Guid.NewGuid() };
    private static object Reason(string reason) => new { requestId = Guid.NewGuid(), lyDo = reason };

    private static MultipartFormDataContent Form(object payload, string? photoField = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"), "payload");
        if (photoField is not null)
        {
            var photo = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52]);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(photo, photoField, "hotel.png");
        }
        return form;
    }

    private static async Task<T> PostFormAsync<T>(HttpClient client, string path, object payload,
        string? photoField = null, HttpStatusCode status = HttpStatusCode.OK) where T : class
    {
        using var form = Form(payload, photoField);
        return await ReadAsync<T>(await client.PostAsync(path, form), status);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK) where T : class
    {
        using (response)
        {
            // Check the status before decoding: unimplemented routes return an empty 404, not an API envelope.
            Assert.True(response.StatusCode == status,
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: expected {(int)status}, got {(int)response.StatusCode}. " +
                await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
            Assert.NotNull(body);
            Assert.True(body.Success, $"{body.ErrorCode}: {body.Message}");
            return Assert.IsType<T>(body.Data);
        }
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.NotNull(body);
            Assert.False(body.Success);
            Assert.Equal(code, body.ErrorCode);
        }
    }

    private sealed class Flow : IAsyncDisposable
    {
        public required PostgresTestDatabase Database { get; init; }
        public required PostgresWebApplicationFactory Postgres { get; init; }
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required HttpClient Customer { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Reception { get; init; }
        public required HttpClient Technician { get; init; }
        public required CareTestSupport.Clock Clock { get; init; }
        public int AdminId { get; private set; }
        public int TechnicianId { get; private set; }
        public int Morning { get; private set; }
        public int Afternoon { get; private set; }
        public int MorningShift { get; private set; }
        public int Bath { get; private set; }
        public int Ears { get; private set; }
        public int Combo { get; private set; }
        public int Room { get; private set; }

        public static async Task<Flow> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
            var password = SeedApplicationFactory.NewPassword();
            var clock = new CareTestSupport.Clock(BookingTime);
            var factory = postgres.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SeedData:Enabled"] = "true",
                    ["SeedData:Date"] = ClinicDate.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["SeedData:AdminPassword"] = password,
                    ["SeedData:Staff1Password"] = password,
                    ["SeedData:Staff2Password"] = password
                }));
                builder.ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                    services.Replace(ServiceDescriptor.Singleton<IFileStorageService>(new CareTestSupport.Storage()));
                    services.Replace(ServiceDescriptor.Singleton<IEmailSender>(new RecordingEmail()));
                });
            });
            var f = new Flow
            {
                Database = database, Postgres = postgres, Factory = factory, Clock = clock,
                Customer = factory.CreateClient(ClientOptions), Admin = factory.CreateClient(ClientOptions),
                Reception = factory.CreateClient(ClientOptions), Technician = factory.CreateClient(ClientOptions)
            };
            try
            {
                f.AdminId = await LoginAsync(f.Admin, "admin", password);
                await LoginAsync(f.Reception, "staff1", password);
                f.TechnicianId = await LoginAsync(f.Technician, "staff2", password);
                await using var db = database.CreateContext();
                var settings = await db.CauHinhHeThongSet.SingleAsync(c => c.Khoa == "EXTENSION_SETTINGS");
                settings.GiaTri = "{\"otpEmail\":false,\"cocHotel\":true,\"giuCho\":true}";
                await db.SaveChangesAsync();
                var catalog = await ReadAsync<CatalogDto>(await f.Customer.GetAsync("/api/catalog"));
                f.Bath = Assert.Single(catalog.DichVu, s => s.TenDichVu == "Tắm sấy vệ sinh").MaDichVu;
                f.Ears = Assert.Single(catalog.DichVu, s => s.TenDichVu == "Vệ sinh tai").MaDichVu;
                f.Combo = Assert.Single(catalog.Combo, c => c.TenCombo == "Combo Tắm Cắt Toàn Diện").MaCombo;
                f.Room = Assert.Single(catalog.LoaiPhong, r => r.TenPhong == "Phòng Tiêu Chuẩn").MaLoaiPhong;
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        public async Task ScheduleAsync()
        {
            var window = await ReadAsync<ShiftWindowDto>(await Admin.PostAsJsonAsync("/api/admin/shift-windows", new
            {
                requestId = Guid.NewGuid(), tuNgay = ClinicDate, denNgay = ClinicDate,
                moAt = Clock.UtcNow.AddHours(-1), dongAt = Clock.UtcNow.AddHours(1)
            }), HttpStatusCode.Created);
            await using var db = Database.CreateContext();
            var shifts = await db.CaLamViecSet.OrderBy(s => s.GioBatDau).ToListAsync();
            MorningShift = shifts[0].Id;
            foreach (var shift in shifts)
            {
                var registered = await ReadAsync<ShiftRegistrationDto>(await Technician.PostAsJsonAsync("/api/internal/shift-registrations",
                    new { requestId = Guid.NewGuid(), ngay = ClinicDate, maCa = shift.Id }), HttpStatusCode.Created);
                Assert.Equal("PENDING", registered.TrangThai);
                var approved = await ReadAsync<ShiftRegistrationDto>(await Admin.PostAsJsonAsync(
                    $"/api/admin/shift-registrations/{registered.MaDangKy}/decision",
                    new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" }));
                Assert.Equal("APPROVED", approved.TrangThai);
                var saved = await db.DangKyCaSet.AsNoTracking().SingleAsync(r => r.Id == registered.MaDangKy);
                Assert.Equal(window.MaDot, saved.DotId);
                Assert.Equal(ShiftRegistrationState.APPROVED, saved.TrangThai);
            }
            // Admin is a valid initial assignee for E15; E16 transfers real ownership to Staff 2.
            var adminShift = await ReadAsync<ShiftRegistrationDto>(await Admin.PostAsJsonAsync("/api/internal/shift-registrations",
                new { requestId = Guid.NewGuid(), ngay = ClinicDate, maCa = MorningShift }), HttpStatusCode.Created);
            await ReadAsync<ShiftRegistrationDto>(await Admin.PostAsJsonAsync(
                $"/api/admin/shift-registrations/{adminShift.MaDangKy}/decision",
                new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" }));
            var schedule = await ReadAsync<List<ShiftRegistrationDto>>(await Technician.GetAsync(
                $"/api/internal/work-schedule?tuNgay={ClinicDate:yyyy-MM-dd}&denNgay={ClinicDate:yyyy-MM-dd}"));
            Assert.Equal(2, schedule.Count);
            Assert.All(schedule, r => { Assert.Equal(TechnicianId, r.MaNhanVien); Assert.Equal("APPROVED", r.TrangThai); });
            var slots = await db.KhungGioDatLichSet.Where(s => s.Ngay == ClinicDate).OrderBy(s => s.CaLamViec.GioBatDau).ToListAsync();
            Morning = slots[0].Id;
            Afternoon = slots[1].Id;
        }

        public PetSelection Pet(string name, int slot, bool hotel, bool combo) => new(4m,
            [combo ? new ServiceSelection { MaCombo = Combo, MaKhungGio = slot } : new ServiceSelection { MaDichVu = Bath, MaKhungGio = slot }],
            TenThuCung: name, LoaiPet: "CHO", MaLoaiPhong: hotel ? Room : null);

        public Task<BookingResponse> BookAsync(HttpClient client, string route, List<PetSelection> pets) =>
            PostFormAsync<BookingResponse>(client, route,
                new BookingCreate(Guid.NewGuid(), Contact, Morning, ClinicDate.AddDays(2), pets), status: HttpStatusCode.Created);

        public async Task ReceiveDepositAsync(BookingResponse booking, decimal amount)
        {
            var receipt = await ReadAsync<DepositDto>(await Reception.PostAsJsonAsync(
                $"/api/internal/deposits/{booking.TienCoc!.MaTienCoc}/receipts",
                new { requestId = Guid.NewGuid(), soTien = amount, phuongThuc = "CASH", giaoDichAt = Clock.UtcNow }), HttpStatusCode.Created);
            Assert.Equal("DA_DAP_UNG", receipt.TrangThaiYeuCau);
            Assert.Equal(amount, receipt.ThucNhan);
            await using var db = Database.CreateContext();
            Assert.Equal(DepositRequestState.DA_DAP_UNG, (await db.TienCocSet.SingleAsync(d => d.Id == receipt.MaTienCoc)).TrangThaiYeuCau);
            Assert.Equal(amount, await db.GiaoDichThuSet.Where(r => r.TienCocId == receipt.MaTienCoc).SumAsync(r => r.SoTien));
        }

        public async Task ConfirmAsync(int id) => Assert.Equal("CONFIRMED", (await ReadAsync<BookingResponse>(
            await Reception.PostAsJsonAsync($"/api/internal/bookings/{id}/confirm", Command()))).TrangThai);

        public Task<BookingResponse> DetailAsync(int id) => ReadAsync<BookingResponse>(Reception.GetAsync($"/api/internal/bookings/{id}"));

        public Task<BookingPet> CheckinAsync(int petId, int assignee, string fur = "BINH_THUONG") =>
            PostFormAsync<BookingPet>(Reception, $"/api/internal/booking-pets/{petId}/check-in", new
            {
                requestId = Guid.NewGuid(), canNangThucTe = 4m, maNhanVien = assignee, tinhTrangLong = new[] { fur },
                ghiChuSucKhoe = "Khỏe mạnh", quyetDinhGia = "SIZE_THUC_TE", xacNhanKhach = true
            }, "anhTiepNhan");

        public Task<BookingPet> CareAsync(int petId, string action) => PostFormAsync<BookingPet>(Technician,
            $"/api/internal/booking-pets/{petId}/care-actions", new { requestId = Guid.NewGuid(), hanhDong = action },
            action == "READY" ? "anhThanhPham" : null);

        public Task<OrderService> CompleteAsync(OrderService line, int componentId) => ReadAsync<OrderService>(Technician.PostAsJsonAsync(
            $"/api/internal/order-services/{line.MaDichVuTrongDon}/components/{componentId}/complete", Command()));

        public Task<BookingPet> CancelAsync(int petId, int nights, int[] components) => ReadAsync<BookingPet>(Admin.PostAsJsonAsync(
            $"/api/admin/booking-pets/{petId}/cancel", new
            { requestId = Guid.NewGuid(), lyDo = "Khách hủy phần chưa sử dụng", soDemDaO = nights, maThanhPhanDaLam = components }));

        public Task<InvoiceResponseDto> InvoiceAsync(int bookingId) => ReadAsync<InvoiceResponseDto>(Reception.PostAsJsonAsync(
            $"/api/internal/bookings/{bookingId}/invoices", Command()), HttpStatusCode.Created);

        public Task<FeeDto> FeeAsync(int bookingId, int? petId, string name, decimal amount) => ReadAsync<FeeDto>(Reception.PostAsJsonAsync(
            $"/api/internal/bookings/{bookingId}/fees", new
            { requestId = Guid.NewGuid(), maChiTietDon = petId, tenPhuPhi = name, soTien = amount, lyDo = "Đối soát với khách" }, Json), HttpStatusCode.Created);

        public Task<TrackingResponse> TrackAsync(BookingResponse booking) => ReadAsync<TrackingResponse>(Customer.PostAsJsonAsync(
            "/api/tracking/lookup", new TrackingPair(Contact.SoDienThoai, booking.TrackingCode)));

        public async Task AssertStateAsync(int id, BookingState expected)
        {
            await using var db = Database.CreateContext();
            Assert.Equal(expected, (await db.DonDatLichSet.SingleAsync(b => b.Id == id)).TrangThai);
        }

        public async ValueTask DisposeAsync()
        {
            Customer.Dispose(); Admin.Dispose(); Reception.Dispose(); Technician.Dispose();
            await Factory.DisposeAsync(); await Postgres.DisposeAsync(); await Database.DisposeAsync();
        }

        private static async Task<int> LoginAsync(HttpClient client, string username, string password)
        {
            var session = await ReadAsync<Session>(await client.PostAsJsonAsync("/api/auth/login", new Login { TenDangNhap = username, MatKhau = password }));
            Assert.Equal(username, session.NhanVien.TenDangNhap);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            return session.NhanVien.MaNhanVien;
        }
    }

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> response, HttpStatusCode status = HttpStatusCode.OK) where T : class =>
        await ReadAsync<T>(await response, status);

    private sealed class RecordingEmail : IEmailSender
    {
        public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken ct) => Task.CompletedTask;
        public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken ct) => Task.CompletedTask;
        public Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct) => Task.CompletedTask;
        public Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount, DateTimeOffset refundedAt, CancellationToken ct) => Task.CompletedTask;
    }
}
