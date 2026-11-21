using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Enums;
using PetSpa.Tests.Care;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Flows;

public class SpaFlowTests
{
    private static readonly DateOnly ClinicDate = new(2026, 11, 20);
    // Freeze the Care clock at 08:30 in Asia/Ho_Chi_Minh, during the approved morning shift.
    private static readonly DateTimeOffset CheckinTime = new(2026, 11, 20, 1, 30, 0, TimeSpan.Zero);
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly byte[] PngHeader =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly Contact Customer = new("Khách đặt lịch", "0912345678", "spa@example.test");
    private static readonly Contact OtherCustomer = new("Khách đặt ca chiều", "0987654321", "other@example.test");
    private const string BathName = "Tắm sấy vệ sinh";

    [Fact]
    public async Task TwoPets_KeepBookedPricesAndCompleteOnlyAfterPaidFinalHandover()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        var password = SeedApplicationFactory.NewPassword();
        var storage = new CareTestSupport.Storage();
        var email = new RecordingEmail();
        await using var factory = Configure(postgres, password, storage, email);
        using var customer = factory.CreateClient(ClientOptions);
        using var reception = factory.CreateClient(ClientOptions);
        using var technician = factory.CreateClient(ClientOptions);
        await SignInAsync(reception, "staff1", password); // E01
        var assignee = await SignInAsync(technician, "staff2", password);

        var catalog = await ReadAsync<CatalogDto>(await customer.GetAsync("/api/catalog")); // E02
        var bath = Assert.Single(catalog.DichVu, service => service.TenDichVu == BathName);
        Assert.Equal(60, bath.ThoiLuongPhut);
        Assert.Equal(150_000m, Assert.Single(bath.BangGia, price => price.MaSize == "S").DonGia);
        Assert.Equal(250_000m, Assert.Single(bath.BangGia, price => price.MaSize == "L").DonGia);
        var (morning, afternoon) = await ReadSlotIdsAsync(database);
        var pets = new List<PetSelection>
        {
            Pet("Milo", 4m, morning, bath.MaDichVu),
            Pet("Nâu", 8m, morning, bath.MaDichVu)
        };
        var available = await ReadAsync<AvailabilityResponse>(await customer.PostAsJsonAsync("/api/availability",
            new AvailabilityQuery
            {
                MaKhungGio = morning, NgayTra = ClinicDate,
                ThuCung = pets.Select(pet => new PetDemand { DichVu = pet.DichVu }).ToList()
            })); // E03
        Assert.True(available.CoTheDat);
        Assert.Equal(morning, available.MaKhungGio);
        Assert.Equal((10, 240), (available.SlotConLai, available.PhutConLai));

        var booking = await CreateBookingAsync(customer, Customer, morning, pets); // E04
        Assert.Equal("PENDING", booking.TrangThai);
        Assert.Equal("SENT", booking.EmailDelivery);
        Assert.Equal(350_000m, booking.TongTamTinh);
        Assert.Equal(2, booking.ThuCung.Count);
        Assert.All(booking.ThuCung, pet => Assert.Equal("NOT_ARRIVED", pet.TrangThai));
        var milo = Assert.Single(booking.ThuCung, pet => pet.TenThuCung == "Milo").MaChiTietDon;
        var nau = Assert.Single(booking.ThuCung, pet => pet.TenThuCung == "Nâu").MaChiTietDon;
        var pair = new TrackingPair(Customer.SoDienThoai, booking.TrackingCode);
        Assert.Equal("PENDING", (await TrackAsync(customer, pair)).TrangThai); // E08

        var pending = await ReadAsync<PageBooking>(await reception.GetAsync("/api/internal/bookings")); // E10a
        Assert.Equal(1, pending.TongSo);
        Assert.Equal(booking.MaDon, Assert.Single(pending.Items).MaDon);
        var bookingPath = $"/api/internal/bookings/{booking.MaDon}";
        var confirmed = await ReadAsync<BookingResponse>(
            await reception.PostAsJsonAsync($"{bookingPath}/confirm", Command())); // E11
        Assert.Equal("CONFIRMED", confirmed.TrangThai);
        Assert.Equal("CONFIRMED", (await TrackAsync(customer, pair)).TrangThai);
        Assert.Collection(email.Deliveries,
            sent => Assert.True(sent == ("tracking", Customer.Email, booking.TrackingCode)),
            sent => Assert.True(sent == ("confirmed", Customer.Email, booking.TrackingCode)));

        // A price and name change must not reprice or rename the already reserved work.
        await using (var catalogDb = database.CreateContext())
        {
            var service = await catalogDb.DichVuSet.SingleAsync(service => service.Id == bath.MaDichVu);
            service.TenDichVu = "Tắm sấy theo bảng giá mới";
            foreach (var price in await catalogDb.BangGiaSizeSet.Where(price => price.DichVuId == bath.MaDichVu).ToListAsync())
                price.DonGia = 999_000m;
            await catalogDb.SaveChangesAsync();
        }

        var firstCheckin = await CheckInAsync(reception, milo, assignee.MaNhanVien, 7.5m, keepBookedSize: true); // E15
        AssertCheckin(firstCheckin, 7.5m, "M", "S", 150_000m);
        Assert.Equal("IN_PROGRESS", (await TrackAsync(customer, pair)).TrangThai);
        var secondCheckin = await CheckInAsync(reception, nau, assignee.MaNhanVien, 16m);
        AssertCheckin(secondCheckin, 16m, "L", "L", 250_000m);

        foreach (var petId in new[] { milo, nau })
        {
            var path = $"/api/internal/booking-pets/{petId}";
            var started = await PostFormAsync<BookingPet>(technician, $"{path}/care-actions",
                new { requestId = Guid.NewGuid(), hanhDong = "START" }); // E17
            Assert.Equal("IN_PROGRESS", started.TrangThai);
        }
        var progress = await PostFormAsync<CareLog>(technician, $"/api/internal/booking-pets/{milo}/care-logs",
            new { requestId = Guid.NewGuid(), ghiChu = "Đang sấy lông", canNangTheoDoi = 7.4m },
            "anh", HttpStatusCode.Created); // E18
        Assert.Equal("CARE_LOG", progress.LoaiNhatKy);
        Assert.Equal(7.4m, progress.CanNangTheoDoi);
        AssertSignedPhoto(progress);
        foreach (var petId in new[] { milo, nau })
        {
            var ready = await PostFormAsync<BookingPet>(technician, $"/api/internal/booking-pets/{petId}/care-actions",
                new { requestId = Guid.NewGuid(), hanhDong = "READY", ghiChu = "Ghi chú kỹ thuật nội bộ" }, "anhThanhPham");
            Assert.Equal("READY", ready.TrangThai);
            Assert.Equal("HOAN_TAT", Assert.Single(ready.DichVu).TrangThai);
            AssertSignedPhoto(Assert.Single(ready.NhatKy, log => log.LoaiNhatKy == "CARE_READY"));
        }

        var otherBooking = await CreateBookingAsync(customer, OtherCustomer, afternoon,
            [Pet("Bông", 4m, afternoon, bath.MaDichVu)]);
        var readsBeforeTracking = storage.Reads.Count;
        await AssertErrorAsync(await customer.PostAsJsonAsync("/api/tracking/lookup",
            new TrackingPair(OtherCustomer.SoDienThoai, booking.TrackingCode)), HttpStatusCode.NotFound, "TRACKING_NOT_FOUND");
        var otherTracking = await TrackAsync(customer, new TrackingPair(OtherCustomer.SoDienThoai, otherBooking.TrackingCode));
        Assert.Equal(otherBooking.MaDon, otherTracking.MaDon);
        Assert.Empty(Assert.Single(otherTracking.ThuCung).NhatKy);
        Assert.Equal(readsBeforeTracking, storage.Reads.Count);

        var trackingData = await ReadAsync<JsonObject>(await customer.PostAsJsonAsync("/api/tracking/lookup", pair));
        var tracking = Assert.IsType<TrackingResponse>(trackingData.Deserialize<TrackingResponse>(JsonOptions));
        var trackingJson = trackingData.ToJsonString();
        Assert.Equal(booking.MaDon, tracking.MaDon);
        Assert.Equal(400_000m, tracking.TongTamTinh);
        await using (var photoDb = database.CreateContext())
        {
            var publicLogs = tracking.ThuCung.SelectMany(pet => pet.NhatKy).ToArray();
            var savedPhotos = await photoDb.NhatKyChamSocSet
                .Where(log => log.ChiTietDon.DonDatLichId == booking.MaDon && log.ObjectPath != null).ToListAsync();
            Assert.Equal(7, savedPhotos.Count); // Two check-ins, two starts, two finished photos, one progress photo.
            foreach (var pet in tracking.ThuCung)
            {
                var expectedLogIds = savedPhotos.Where(log => log.ChiTietDonId == pet.MaChiTietDon)
                    .Select(log => log.Id).Order().ToArray();
                Assert.Equal(expectedLogIds, pet.NhatKy.Select(log => log.MaNhatKy).Order().ToArray());
            }
            var publicProgress = Assert.Single(publicLogs, log => log.LoaiNhatKy == "CARE_LOG");
            Assert.Equal(progress.MaNhatKy, publicProgress.MaNhatKy);
            Assert.Equal("Đang sấy lông", publicProgress.GhiChu);
            Assert.All(publicLogs.Where(log => log.LoaiNhatKy != "CARE_LOG"), log => Assert.Null(log.GhiChu));
            // The storage double numbers read URLs in the order it signs the private paths.
            var signedUrls = storage.Reads.Skip(readsBeforeTracking).Select((path, index) =>
                (Path: path, Url: $"https://photo.test/sign/{readsBeforeTracking + index + 1}"))
                .ToDictionary(item => item.Path, item => item.Url, StringComparer.Ordinal);
            Assert.Equal(savedPhotos.Select(log => log.ObjectPath!).Distinct().Order(StringComparer.Ordinal),
                signedUrls.Keys.Order(StringComparer.Ordinal));
            foreach (var savedPhoto in savedPhotos)
            {
                var log = Assert.Single(publicLogs, log => log.MaNhatKy == savedPhoto.Id);
                Assert.Equal(signedUrls[savedPhoto.ObjectPath!], log.AnhUrl);
                Assert.False(trackingJson.Contains(savedPhoto.ObjectPath!, StringComparison.Ordinal),
                    "The tracking response exposed a private object path.");
            }
        }

        var issued = await ReadAsync<InvoiceResponseDto>(
            await reception.PostAsJsonAsync($"{bookingPath}/invoices", Command()), HttpStatusCode.Created); // E23
        Assert.Equal("UNPAID", issued.TrangThai);
        Assert.Equal(400_000m, issued.TongTien);
        Assert.Equal(400_000m, issued.ConPhaiTra);
        Assert.Equal(2, issued.DongTien.Count);
        AssertInvoiceLine(issued, milo, "Milo", "S", 150_000m);
        AssertInvoiceLine(issued, nau, "Nâu", "L", 250_000m);
        var invoicePath = $"/api/internal/invoices/{issued.MaHoaDon}";
        var viewed = await ReadAsync<InvoiceResponseDto>(await reception.GetAsync(invoicePath)); // E24
        Assert.Equal(issued.MaHoaDon, viewed.MaHoaDon);
        Assert.Equal(issued.DongTien.ToArray(), viewed.DongTien.ToArray());
        Assert.Equal("UNPAID", (await TrackAsync(customer, pair)).HoaDon?.TrangThai);
        await AssertErrorAsync(await SendHandoverAsync(reception, milo), HttpStatusCode.Conflict, "INVALID_TRANSITION");

        var paid = await ReadAsync<InvoiceResponseDto>(await reception.PostAsJsonAsync($"{invoicePath}/checkout",
            new { requestId = Guid.NewGuid(), phuongThuc = "CASH", soTienThucNhan = 400_000, soTienDaTraLai = 0 })); // E26
        Assert.Equal("PAID", paid.TrangThai);
        Assert.NotNull(paid.PaidAt);
        Assert.Equal(issued.DongTien.ToArray(), paid.DongTien.ToArray());
        var paidTracking = await TrackAsync(customer, pair);
        Assert.Equal("PAID", paidTracking.HoaDon?.TrangThai);
        Assert.Equal("IN_PROGRESS", paidTracking.TrangThai);
        Assert.All(paidTracking.ThuCung, pet => Assert.Equal("READY", pet.TrangThai));

        var handedOver = await ReadAsync<BookingPet>(await SendHandoverAsync(reception, milo)); // E19
        Assert.Equal("DONE", handedOver.TrangThai);
        Assert.NotNull(handedOver.BanGiaoAt);
        var partial = await TrackAsync(customer, pair);
        Assert.Equal("IN_PROGRESS", partial.TrangThai);
        Assert.Equal("READY", Assert.Single(partial.ThuCung, pet => pet.MaChiTietDon == nau).TrangThai);

        Assert.Equal("DONE", (await ReadAsync<BookingPet>(await SendHandoverAsync(reception, nau))).TrangThai);
        var completed = await TrackAsync(customer, pair);
        Assert.Equal("COMPLETED", completed.TrangThai);
        Assert.All(completed.ThuCung, pet => Assert.Equal("DONE", pet.TrangThai));
        await using var db = database.CreateContext();
        var savedBooking = await db.DonDatLichSet.Include(item => item.ChiTietDonDonDatLichItems)
            .SingleAsync(item => item.Id == booking.MaDon);
        Assert.Equal(BookingState.COMPLETED, savedBooking.TrangThai);
        Assert.Equal(CheckinTime, savedBooking.HoanTatAt);
        Assert.Equal(2, savedBooking.ChiTietDonDonDatLichItems.Count);
        Assert.All(savedBooking.ChiTietDonDonDatLichItems, pet =>
        {
            Assert.Equal(PetCareState.DONE, pet.TrangThaiChamSoc);
            Assert.Equal(CheckinTime, pet.CheckInAt);
            Assert.NotNull(pet.BanGiaoAt);
        });
        var savedInvoice = await db.HoaDonSet.Include(invoice => invoice.CtHoaDonHoaDonItems)
            .SingleAsync(invoice => invoice.DonDatLichId == booking.MaDon);
        Assert.Equal(InvoiceState.PAID, savedInvoice.TrangThai);
        Assert.Equal(new[] { 150_000m, 250_000m },
            savedInvoice.CtHoaDonHoaDonItems.Select(line => line.DonGiaSnapshot).Order().ToArray());
        var receipt = await db.GiaoDichThuSet.SingleAsync(receipt => receipt.HoaDonId == issued.MaHoaDon);
        Assert.Equal(400_000m, receipt.SoTien);
    }

    [Fact]
    public async Task RejectingPendingBooking_ReleasesItsOriginalCapacityOnceAndBlocksCheckin()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        var password = SeedApplicationFactory.NewPassword();
        var storage = new CareTestSupport.Storage();
        await using var factory = Configure(postgres, password, storage, new RecordingEmail());
        using var customer = factory.CreateClient(ClientOptions);
        using var reception = factory.CreateClient(ClientOptions);
        using var technician = factory.CreateClient(ClientOptions);
        await SignInAsync(reception, "staff1", password);
        var assignee = await SignInAsync(technician, "staff2", password);
        var (morning, afternoon) = await ReadSlotIdsAsync(database);
        var catalog = await ReadAsync<CatalogDto>(await customer.GetAsync("/api/catalog"));
        var bath = Assert.Single(catalog.DichVu, service => service.TenDichVu == BathName).MaDichVu;
        var ears = Assert.Single(catalog.DichVu, service => service.TenDichVu == "Vệ sinh tai").MaDichVu;
        var remaining = await CreateBookingAsync(customer, OtherCustomer, afternoon, [Pet("Bông", 4m, afternoon, bath)]);
        var originalCapacity = await ReadCapacityAsync(database);
        Assert.Equal(new[] { (morning, 0, 0), (afternoon, 1, 60) }, originalCapacity);

        var target = await CreateBookingAsync(customer, Customer, morning, [Pet("Milo", 4m, afternoon, bath, ears)]);
        var petId = Assert.Single(target.ThuCung).MaChiTietDon;
        Assert.Equal("PENDING", target.TrangThai);
        Assert.Equal(new[] { (morning, 0, 0), (afternoon, 2, 150) }, await ReadCapacityAsync(database));
        await using (var setup = database.CreateContext())
        {
            var reservations = await setup.DichVuTrongDonSet.Where(line => line.ChiTietDonId == petId)
                .OrderBy(line => line.ThoiLuongSnapshot).ToListAsync();
            Assert.Equal(new[] { (afternoon, 30), (afternoon, 60) },
                reservations.Select(line => (line.KhungGioId, line.ThoiLuongSnapshot)).ToArray());
            foreach (var service in await setup.DichVuSet.Where(service => service.Id == bath || service.Id == ears).ToListAsync())
                service.ThoiLuongPhut += 15;
            await setup.SaveChangesAsync();
        }

        var request = new CancelBooking(Guid.NewGuid(), "STAFF_REJECTED", "Không thể tiếp nhận yêu cầu của khách");
        var cancelPath = $"/api/internal/bookings/{target.MaDon}/cancel";
        var rejected = await ReadAsync<BookingResponse>(await reception.PostAsJsonAsync(cancelPath, request)); // E12
        Assert.Equal(target.MaDon, rejected.MaDon);
        Assert.Equal("CANCELLED", rejected.TrangThai);
        Assert.Equal(originalCapacity, await ReadCapacityAsync(database));

        var replay = await ReadAsync<BookingResponse>(await reception.PostAsJsonAsync(cancelPath, request));
        Assert.Equal(rejected.MaDon, replay.MaDon);
        Assert.Equal("CANCELLED", replay.TrangThai);
        Assert.Equal(originalCapacity, await ReadCapacityAsync(database));
        Assert.Equal("CANCELLED", (await TrackAsync(customer, new TrackingPair(Customer.SoDienThoai, target.TrackingCode))).TrangThai);
        Assert.Equal("PENDING", (await TrackAsync(customer, new TrackingPair(OtherCustomer.SoDienThoai, remaining.TrackingCode))).TrangThai);

        using var checkin = Form(CheckinPayload(assignee.MaNhanVien, 4m, keepBookedSize: false), "anhTiepNhan");
        await AssertErrorAsync(await reception.PostAsync($"/api/internal/booking-pets/{petId}/check-in", checkin),
            HttpStatusCode.Conflict, "INVALID_TRANSITION");
        Assert.Empty(storage.Uploads);
        await using var db = database.CreateContext();
        var savedPet = await db.ChiTietDonSet.Include(pet => pet.DonDatLich).SingleAsync(pet => pet.Id == petId);
        Assert.Equal(BookingState.CANCELLED, savedPet.DonDatLich.TrangThai);
        Assert.Equal(PetCareState.NOT_ARRIVED, savedPet.TrangThaiChamSoc);
        Assert.Null(savedPet.CheckInAt);
        Assert.False(await db.NhatKyChamSocSet.AnyAsync(log => log.ChiTietDonId == petId));
        Assert.Equal(1, await db.AuditLogSet.CountAsync(audit => audit.HanhDong == "BOOKING_CANCEL" &&
            audit.DoiTuongId == target.MaDon && audit.RequestId == request.RequestId));
        Assert.Equal(originalCapacity, await ReadCapacityAsync(database));
    }

    private static WebApplicationFactory<Program> Configure(
        PostgresWebApplicationFactory postgres, string password, CareTestSupport.Storage storage, RecordingEmail email) =>
        postgres.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedData:Enabled"] = "true",
                ["SeedData:Date"] = ClinicDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["SeedData:AdminPassword"] = password,
                ["SeedData:Staff1Password"] = password,
                ["SeedData:Staff2Password"] = password
            }));
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new CareTestSupport.Clock(CheckinTime)));
                services.Replace(ServiceDescriptor.Singleton<IFileStorageService>(storage));
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(email));
            });
        });

    private static async Task<(int Morning, int Afternoon)> ReadSlotIdsAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateContext();
        var slots = await db.KhungGioDatLichSet.Where(slot => slot.Ngay == ClinicDate)
            .OrderBy(slot => slot.CaLamViec.GioBatDau).Select(slot => slot.Id).ToArrayAsync();
        Assert.Equal(2, slots.Length);
        return (slots[0], slots[1]);
    }

    private static async Task<(int Slot, int Seats, int Minutes)[]> ReadCapacityAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateContext();
        var slots = await db.KhungGioDatLichSet.Where(slot => slot.Ngay == ClinicDate).OrderBy(slot => slot.CaLamViec.GioBatDau)
            .Select(slot => new { slot.Id, slot.SlotDaDat, slot.PhutDaGiu }).ToListAsync();
        return slots.Select(slot => (slot.Id, slot.SlotDaDat, slot.PhutDaGiu)).ToArray();
    }

    private static async Task<Employee> SignInAsync(HttpClient client, string username, string password)
    {
        var session = await ReadAsync<Session>(await client.PostAsJsonAsync("/api/auth/login",
            new Login { TenDangNhap = username, MatKhau = password }));
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.Equal(username, session.NhanVien.TenDangNhap);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session.NhanVien;
    }

    private static PetSelection Pet(string name, decimal weight, int slot, params int[] services) =>
        new(weight, services.Select(id => new ServiceSelection { MaDichVu = id, MaKhungGio = slot }).ToList(),
            TenThuCung: name, LoaiPet: "CHO");

    private static Task<BookingResponse> CreateBookingAsync(HttpClient client, Contact contact, int slot, List<PetSelection> pets) =>
        PostFormAsync<BookingResponse>(client, "/api/bookings", new BookingCreate(Guid.NewGuid(), contact, slot, ClinicDate, pets),
            status: HttpStatusCode.Created);

    private static async Task<TrackingResponse> TrackAsync(HttpClient client, TrackingPair pair) =>
        await ReadAsync<TrackingResponse>(await client.PostAsJsonAsync("/api/tracking/lookup", pair));

    private static Task<BookingPet> CheckInAsync(
        HttpClient client, int petId, int assignee, decimal weight, bool keepBookedSize = false) =>
        PostFormAsync<BookingPet>(client, $"/api/internal/booking-pets/{petId}/check-in",
            CheckinPayload(assignee, weight, keepBookedSize), "anhTiepNhan");

    private static object CheckinPayload(int assignee, decimal weight, bool keepBookedSize) => new
    {
        requestId = Guid.NewGuid(), canNangThucTe = weight, maNhanVien = assignee,
        tinhTrangLong = new[] { "LONG_DAI", "ROI_NHE" }, ghiChuSucKhoe = "Bé khỏe mạnh",
        quyetDinhGia = keepBookedSize ? "THU_GON" : "SIZE_THUC_TE", xacNhanKhach = true,
        noiDungThuGon = keepBookedSize ? "Giữ giá đã đặt, giảm phần chải lông" : null
    };

    private static void AssertCheckin(BookingPet pet, decimal weight, string measuredSize, string committedSize, decimal price)
    {
        Assert.Equal("WAITING", pet.TrangThai);
        Assert.Equal(weight, pet.CanNangChot);
        Assert.Equal(measuredSize, pet.SizeThucTe);
        Assert.Equal(committedSize, pet.SizeChot);
        Assert.Equal(new[] { "LONG_DAI", "ROI_NHE" }, pet.TinhTrangLong);
        var service = Assert.Single(pet.DichVu);
        Assert.Equal(BathName, service.TenDichVu);
        Assert.Equal(committedSize, service.SizeChot);
        Assert.Equal(price, service.GiaChot);
        AssertSignedPhoto(Assert.Single(pet.NhatKy, log => log.LoaiNhatKy == "CHECK_IN"));
    }

    private static void AssertSignedPhoto(CareLog log) => Assert.StartsWith("https://photo.test/sign/", log.AnhUrl);

    private static void AssertInvoiceLine(InvoiceResponseDto invoice, int petId, string petName, string size, decimal price)
    {
        var line = Assert.Single(invoice.DongTien, line => line.MaChiTietDon == petId);
        Assert.Equal("SPA", line.LoaiKhoan);
        Assert.Equal(petName, line.TenPet);
        Assert.Equal(BathName, line.TenKhoan);
        Assert.Equal(size, line.Size);
        Assert.Equal(1, line.SoLuong);
        Assert.Equal(price, line.DonGia);
        Assert.Equal(price, line.ThanhTien);
    }

    private static Task<HttpResponseMessage> SendHandoverAsync(HttpClient client, int petId) =>
        client.PostAsJsonAsync($"/api/internal/booking-pets/{petId}/handover",
            new { requestId = Guid.NewGuid(), xacNhanDaNhan = true });

    private static BookingCommand Command() => new(Guid.NewGuid());

    private static async Task<T> PostFormAsync<T>(HttpClient client, string path, object payload, string? photoField = null,
        HttpStatusCode status = HttpStatusCode.OK) where T : class
    {
        using var form = Form(payload, photoField);
        return await ReadAsync<T>(await client.PostAsync(path, form), status);
    }

    private static MultipartFormDataContent Form(object payload, string? photoField)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), "payload");
        if (photoField is not null)
        {
            var file = new ByteArrayContent(PngHeader);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, photoField, "spa.png");
        }
        return form;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
        where T : class
    {
        using (response)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
            Assert.True(response.StatusCode == status,
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} expected HTTP {(int)status}, " +
                $"received {(int)response.StatusCode} ({result?.ErrorCode}). {result?.Message}");
            Assert.NotNull(result);
            Assert.True(result.Success);
            return Assert.IsType<T>(result.Data);
        }
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Equal(code, result.ErrorCode);
        }
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public List<(string Kind, string Recipient, string Code)> Deliveries { get; } = [];

        public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken)
        {
            Deliveries.Add(("tracking", recipient, trackingCode));
            return Task.CompletedTask;
        }

        public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken)
        {
            Deliveries.Add(("confirmed", recipient, trackingCode));
            return Task.CompletedTask;
        }
    }
}
