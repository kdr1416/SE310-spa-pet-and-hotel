using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public sealed class BookingCreateEndpointTests
{
    private static readonly DateOnly Date = new(2036, 11, 20);
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00];

    [Fact]
    public async Task CreatesPendingBookingAndReservesOneSeatForTwoServicesOnTheSamePet()
    {
        await using var fixture = await Fixture.CreateAsync();
        var payload = Payload(Guid.NewGuid());
        Pet(payload)["dichVu"]!.AsArray().Add(Service(2));

        var data = await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.Created);

        Assert.Matches("^[A-Z0-9]{8}$", data["trackingCode"]!.GetValue<string>());
        Assert.Equal("PENDING", data["trangThai"]!.GetValue<string>());
        Assert.Equal("NOT_ARRIVED", data["thuCung"]![0]!["trangThai"]!.GetValue<string>());
        Assert.Equal("booking@example.test", data["lienHe"]!["email"]!.GetValue<string>());
        Assert.Equal(300_000, data["tongTamTinh"]!.GetValue<decimal>());
        Assert.True(data["maDon"]!.GetValue<int>() > 0);
        Assert.True(data["thuCung"]![0]!["maChiTietDon"]!.GetValue<int>() > 0);
        Assert.True(data["thuCung"]![0]!["dichVu"]![0]!["maDichVuTrongDon"]!.GetValue<int>() > 0);
        Assert.Null(data["thuCung"]![0]!["dichVu"]![0]!["giaChot"]);
        await using var db = fixture.Database.CreateContext();
        var slot = await db.KhungGioDatLichSet.SingleAsync();
        Assert.Equal(1, slot.SlotDaDat);
        Assert.Equal(90, slot.PhutDaGiu);
        var lines = await db.DichVuTrongDonSet.OrderBy(s => s.DichVuId).ToListAsync();
        Assert.Equal(new[] { 60, 30 }, lines.Select(s => s.ThoiLuongSnapshot));
        var prices = JsonNode.Parse(lines[0].BangGiaSizeSnapshot)!;
        Assert.Equal(4, prices.AsObject().Count);
        Assert.Equal(200_000, prices["M"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task ReplayReadsTheSameResultBeforeCheckingNewProofRequirementsOrUploadingAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var payload = Payload(Guid.NewGuid());
        Pet(payload)["vaccinationFileKey"] = "vaccine";
        var first = await ReadDataAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.Created);
        await using (var db = fixture.Database.CreateContext())
        {
            var setting = await db.CauHinhHeThongSet.SingleAsync();
            setting.GiaTri = """{"otpEmail":true}""";
            (await db.DonDatLichSet.SingleAsync()).TrangThai = BookingState.CANCELLED;
            await db.SaveChangesAsync();
        }

        var replay = await ReadDataAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.OK);

        Assert.True(JsonNode.DeepEquals(first, replay));
        Assert.Equal(1, fixture.Storage.UploadCount);
        Assert.Empty(fixture.Storage.Deleted);
        var alteredFile = Jpeg.Concat(new byte[] { 0x01 }).ToArray();
        await AssertErrorAsync(await SendAsync(fixture.Client, payload, alteredFile), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        payload["ghiChu"] = "Changed";
        await AssertErrorAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await using var persisted = fixture.Database.CreateContext();
        Assert.Equal(1, await persisted.DonDatLichSet.CountAsync());
        var policy = JsonNode.Parse((await persisted.DonDatLichSet.SingleAsync()).ChinhSachSnapshot)!;
        var pet = await persisted.ChiTietDonSet.SingleAsync();
        Assert.Equal(pet.Id, policy["thuCung"]![0]!["maChiTietDon"]!.GetValue<int>());
        Assert.Equal("test-1.jpg", policy["thuCung"]![0]!["vaccinationObjectPath"]!.GetValue<string>());
        Assert.Null(pet.HoSoTiemChungId);
        Assert.Null(pet.XacMinhTiemAt);
        Assert.DoesNotContain("test-1.jpg", first.ToJsonString());
    }

    [Fact]
    public async Task ReusingCommittedKeyWithoutItsReferencedAttachmentConflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var payload = Payload(Guid.NewGuid());
        Pet(payload)["vaccinationFileKey"] = "vaccine";
        await ReadDataAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.Created);

        await AssertErrorAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");

        payload["requestId"] = Guid.NewGuid().ToString();
        await AssertErrorAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal(1, fixture.Storage.UploadCount);
    }

    [Fact]
    public async Task DuplicateUploadThatFailsAfterOriginalCommitReturnsTheCommittedBooking()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstUploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondUploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failSecondUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Storage.BeforeUpload = async (attempt, cancellationToken) =>
        {
            if (attempt == 1)
            {
                firstUploadStarted.SetResult();
                await finishFirstUpload.Task.WaitAsync(cancellationToken);
                return;
            }

            secondUploadStarted.SetResult();
            await failSecondUpload.Task.WaitAsync(cancellationToken);
            throw new FileStorageException("Test storage outage", "STORAGE_UNAVAILABLE");
        };

        var payload = Payload(Guid.NewGuid());
        Pet(payload)["vaccinationFileKey"] = "vaccine";
        var first = SendAsync(fixture.Client, payload, Jpeg);
        try
        {
            await firstUploadStarted.Task.WaitAsync(timeout.Token);
            var duplicate = SendAsync(fixture.Client, payload, Jpeg);
            await secondUploadStarted.Task.WaitAsync(timeout.Token);
            finishFirstUpload.SetResult();
            var created = await ReadDataAsync(await first.WaitAsync(timeout.Token), HttpStatusCode.Created);
            failSecondUpload.SetResult();

            var replay = await ReadDataAsync(await duplicate.WaitAsync(timeout.Token), HttpStatusCode.OK);

            Assert.True(JsonNode.DeepEquals(created, replay));
            Assert.Equal(1, fixture.Email.Attempts);
            Assert.Empty(fixture.Storage.Deleted);
            await using var db = fixture.Database.CreateContext();
            Assert.Equal(1, await db.DonDatLichSet.CountAsync());
            Assert.Equal(1, (await db.KhungGioDatLichSet.SingleAsync()).SlotDaDat);
        }
        finally
        {
            finishFirstUpload.TrySetResult();
            failSecondUpload.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequestsReserveLastSeatOnce(bool sameRequest)
    {
        await using var fixture = await Fixture.CreateAsync();
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(
            SendAsync(fixture.Client, Payload(key)),
            SendAsync(fixture.Client, Payload(sameRequest ? key : Guid.NewGuid())));
        var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var other = Assert.Single(responses, response => !ReferenceEquals(response, created));
        var data = await ReadDataAsync(created, HttpStatusCode.Created);
        if (sameRequest)
        {
            var replay = await ReadDataAsync(other, HttpStatusCode.OK);
            Assert.Equal(data["maDon"]!.GetValue<int>(), replay["maDon"]!.GetValue<int>());
        }
        else
        {
            await AssertErrorAsync(other, HttpStatusCode.Conflict, "CAPACITY_EXCEEDED");
        }
        await using var db = fixture.Database.CreateContext();
        Assert.Equal(1, await db.DonDatLichSet.CountAsync());
        Assert.Equal(1, (await db.KhungGioDatLichSet.SingleAsync()).SlotDaDat);
        Assert.Equal(60, (await db.KhungGioDatLichSet.SingleAsync()).PhutDaGiu);
    }

    [Theory]
    [InlineData("missing-file", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("unsupported-file", HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_FILE")]
    [InlineData("oversized-file", HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE")]
    [InlineData("otp-required", HttpStatusCode.Forbidden, "PROOF_SCOPE_INVALID")]
    [InlineData("unproved-old-pet", HttpStatusCode.Forbidden, "PROOF_SCOPE_INVALID")]
    [InlineData("proof-wrong-scope", HttpStatusCode.Forbidden, "PROOF_SCOPE_INVALID")]
    [InlineData("vaccine-id-without-proof", HttpStatusCode.Forbidden, "PROOF_SCOPE_INVALID")]
    [InlineData("hotel", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("combo", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("overnight", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("client-price", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("holiday", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("minutes", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    public async Task InvalidBookingDoesNotReserveCapacity(string scenario, HttpStatusCode status, string code)
    {
        await using var fixture = await Fixture.CreateAsync(
            otp: scenario == "otp-required", useRealStorage: scenario == "unsupported-file");
        var payload = Payload(Guid.NewGuid());
        byte[]? file = null;
        switch (scenario)
        {
            case "missing-file":
            case "unsupported-file":
            case "oversized-file":
                Pet(payload)["vaccinationFileKey"] = "vaccine";
                file = scenario switch
                {
                    "unsupported-file" => Encoding.UTF8.GetBytes("not an image"),
                    "oversized-file" => new byte[5 * 1024 * 1024 + 1],
                    _ => null
                };
                break;
            case "unproved-old-pet":
                Pet(payload)["maThuCung"] = 1;
                break;
            case "proof-wrong-scope":
                Pet(payload)["maThuCung"] = 1;
                payload["petProof"] = "invalid-scope";
                break;
            case "vaccine-id-without-proof":
                Pet(payload)["maHoSoTiemChung"] = 1;
                break;
            case "hotel":
                Pet(payload)["maLoaiPhong"] = 1;
                break;
            case "combo":
                Pet(payload)["dichVu"]![0]!.AsObject().Remove("maDichVu");
                Pet(payload)["dichVu"]![0]!["maCombo"] = 1;
                break;
            case "overnight":
                payload["ngayTra"] = Date.AddDays(1).ToString("yyyy-MM-dd");
                break;
            case "client-price":
                payload["tongTamTinh"] = 1;
                break;
            case "holiday":
                await using (var db = fixture.Database.CreateContext())
                {
                    db.NgayNghiSet.Add(new NgayNghi { Ngay = Date, TaoBoiId = 1, LyDo = "Holiday" });
                    await db.SaveChangesAsync();
                }
                break;
            case "minutes":
                await using (var db = fixture.Database.CreateContext())
                {
                    (await db.KhungGioDatLichSet.SingleAsync()).TranPhut = 59;
                    await db.SaveChangesAsync();
                }
                break;
        }

        await AssertErrorAsync(await SendAsync(fixture.Client, payload, file), status, code);
        await using var persisted = fixture.Database.CreateContext();
        Assert.Empty(await persisted.DonDatLichSet.ToListAsync());
        Assert.Equal(0, (await persisted.KhungGioDatLichSet.SingleAsync()).SlotDaDat);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackTheBookingAndCleansUpItsUpload()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dich_vu_trong_don ADD CONSTRAINT e04_test_reject_insert CHECK (false)");
        var payload = Payload(Guid.NewGuid());
        Pet(payload)["vaccinationFileKey"] = "vaccine";

        await AssertErrorAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.ServiceUnavailable, "TEMPORARY_FAILURE");

        Assert.Equal(1, fixture.Storage.UploadCount);
        Assert.Single(fixture.Storage.Deleted);
        Assert.Empty(await db.DonDatLichSet.ToListAsync());
        Assert.Empty(await db.KhachHangSet.ToListAsync());
        Assert.Empty(await db.ThuCungSet.ToListAsync());
        Assert.Empty(await db.HoSoTiemChungSet.ToListAsync());
        Assert.Equal(0, (await db.KhungGioDatLichSet.AsNoTracking().SingleAsync()).SlotDaDat);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dich_vu_trong_don DROP CONSTRAINT e04_test_reject_insert");
        await ReadDataAsync(await SendAsync(fixture.Client, payload, Jpeg), HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeliveryFailureStillReturnsCommittedBookingAndCanBeReplayed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Email.Fail = true;
        var payload = Payload(Guid.NewGuid());
        var created = await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.Created);
        Assert.Equal("FAILED", created["emailDelivery"]!.GetValue<string>());
        var replay = await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.OK);
        Assert.Equal("FAILED", replay["emailDelivery"]!.GetValue<string>());
        await using var db = fixture.Database.CreateContext();
        var booking = await db.DonDatLichSet.SingleAsync();
        Assert.Equal("FAILED", booking.EmailDelivery);
        Assert.NotNull(booking.EmailAttemptAt);
        Assert.True(fixture.Email.BookingWasCommitted);
        Assert.Equal(1, fixture.Email.Attempts);
    }

    [Fact]
    public async Task ReplayWhileEmailIsInProgressUsesFallbackWithoutSendingAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var finishSending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Email.WaitFor = finishSending.Task;
        var payload = Payload(Guid.NewGuid());
        var first = SendAsync(fixture.Client, payload);
        try
        {
            await fixture.Email.Started.Task.WaitAsync(timeout.Token);
            var replay = await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.OK);
            Assert.Equal("FAILED", replay["emailDelivery"]!.GetValue<string>());
        }
        finally
        {
            finishSending.TrySetResult();
        }
        Assert.Equal("SENT", (await ReadDataAsync(await first, HttpStatusCode.Created))["emailDelivery"]!.GetValue<string>());
        Assert.Equal("SENT", (await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.OK))["emailDelivery"]!.GetValue<string>());
        Assert.True(fixture.Email.BookingWasCommitted);
        Assert.Equal(1, fixture.Email.Attempts);
    }

    [Theory]
    [InlineData("matching-proof", HttpStatusCode.Created)]
    [InlineData("another-pets-vaccination", HttpStatusCode.Forbidden)]
    [InlineData("booking-proof-used-as-pet-proof", HttpStatusCode.Forbidden)]
    [InlineData("expired-proof", HttpStatusCode.Forbidden)]
    public async Task ExistingPetAndVaccinationRequireSingleUseProofForTheSameCustomer(string scenario, HttpStatusCode status)
    {
        await using var fixture = await Fixture.CreateAsync(otp: true);
        var payload = Payload(Guid.NewGuid());
        payload["bookingProof"] = "booking-proof-example";
        payload["petProof"] = scenario == "booking-proof-used-as-pet-proof" ? "booking-proof-example" : "pet-proof-example";
        payload["maKhachHang"] = 1;
        Pet(payload)["maThuCung"] = 1;
        Pet(payload)["maHoSoTiemChung"] = 1;
        Pet(payload).Remove("tenThuCung");
        Pet(payload).Remove("loaiPet");
        await using var db = fixture.Database.CreateContext();
        db.KhachHangSet.Add(new KhachHang
        {
            Id = 1, HoTen = "Master owner", SoDienThoai = "0912345678", Email = "booking@example.test"
        });
        db.ThuCungSet.AddRange(
            new ThuCung { Id = 1, KhachHangId = 1, TenThuCung = "Master pet", CanNang = 8 },
            new ThuCung { Id = 2, KhachHangId = 1, TenThuCung = "Other pet", CanNang = 8 });
        db.HoSoTiemChungSet.Add(new HoSoTiemChung
        {
            Id = 1, ThuCungId = scenario == "another-pets-vaccination" ? 2 : 1,
            LoaiVaccine = "Rabies", NgayTiem = new DateOnly(2025, 11, 20)
        });
        db.OtpChallengeSet.AddRange(
            Proof(OtpScope.BOOKING, "booking-proof-example", null, expired: false),
            Proof(OtpScope.EXISTING_PETS, "pet-proof-example", 1, expired: scenario == "expired-proof"));
        await db.SaveChangesAsync();

        var response = await SendAsync(fixture.Client, payload);
        if (status == HttpStatusCode.Forbidden)
        {
            await AssertErrorAsync(response, status, "PROOF_SCOPE_INVALID");
            Assert.Empty(await db.DonDatLichSet.ToListAsync());
            Assert.All(await db.OtpChallengeSet.AsNoTracking().ToListAsync(), proof => Assert.Null(proof.SuDungAt));
            return;
        }

        var result = await ReadDataAsync(response, status);
        Assert.Equal("Master pet", result["thuCung"]![0]!["tenThuCung"]!.GetValue<string>());
        Assert.Equal("Booking contact", result["lienHe"]!["hoTen"]!.GetValue<string>());
        Assert.Equal("Master owner", (await db.KhachHangSet.AsNoTracking().SingleAsync()).HoTen);
        Assert.Equal(1, (await db.ChiTietDonSet.SingleAsync()).HoSoTiemChungId);
        Assert.All(await db.OtpChallengeSet.AsNoTracking().ToListAsync(), proof => Assert.NotNull(proof.SuDungAt));
        await ReadDataAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.OK);
        payload["requestId"] = Guid.NewGuid().ToString();
        await AssertErrorAsync(await SendAsync(fixture.Client, payload), HttpStatusCode.Forbidden, "PROOF_SCOPE_INVALID");
    }

    [Theory]
    [InlineData(3, HttpStatusCode.Created)]
    [InlineData(4, HttpStatusCode.ServiceUnavailable)]
    public async Task TrackingCollisionRetriesAtMostThreeTimes(int collisions, HttpStatusCode status)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE e04_test_attempts");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION e04_test_collision() RETURNS trigger AS $$
            BEGIN
                IF nextval('e04_test_attempts') <= TG_ARGV[0]::integer THEN
                    RAISE unique_violation USING CONSTRAINT = 'ix_don_dat_lich_tracking_code';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        var trigger = collisions == 3 ? """
            CREATE TRIGGER e04_test_collision BEFORE INSERT ON don_dat_lich FOR EACH ROW EXECUTE FUNCTION e04_test_collision('3')
            """ : """
            CREATE TRIGGER e04_test_collision BEFORE INSERT ON don_dat_lich FOR EACH ROW EXECUTE FUNCTION e04_test_collision('4')
            """;
        await db.Database.ExecuteSqlRawAsync(trigger);

        var response = await SendAsync(fixture.Client, Payload(Guid.NewGuid()));

        if (status == HttpStatusCode.Created)
        {
            var booking = await ReadDataAsync(response, status);
            Assert.Matches("^[A-Z0-9]{8}$", booking["trackingCode"]!.GetValue<string>());
            Assert.Equal(1, (await db.KhungGioDatLichSet.SingleAsync()).SlotDaDat);
        }
        else
        {
            await AssertErrorAsync(response, status, "TEMPORARY_FAILURE");
            Assert.Empty(await db.DonDatLichSet.ToListAsync());
            Assert.Equal(0, (await db.KhungGioDatLichSet.SingleAsync()).SlotDaDat);
        }
        var attempts = await db.Database.SqlQueryRaw<long>("SELECT last_value AS \"Value\" FROM e04_test_attempts").SingleAsync();
        Assert.Equal(4, attempts);
    }

    private static OtpChallenge Proof(OtpScope scope, string token, int? customerId, bool expired) => new()
    {
        RequestId = Guid.NewGuid(),
        Email = "booking@example.test",
        SoDienThoai = "0912345678",
        MucDich = scope,
        ProofDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
        OtpDigest = "test-challenge",
        HetHanAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -10 : 10),
        GuiLaiSau = DateTimeOffset.UtcNow,
        KhachHangId = customerId
    };

    private static JsonObject Payload(Guid key) => new()
    {
        ["requestId"] = key.ToString(),
        ["lienHe"] = new JsonObject
        {
            ["hoTen"] = "Booking contact", ["soDienThoai"] = "0912345678", ["email"] = "booking@example.test"
        },
        ["maKhungGio"] = 1,
        ["ngayTra"] = Date.ToString("yyyy-MM-dd"),
        ["thuCung"] = new JsonArray(new JsonObject
        {
            ["tenThuCung"] = "Milo", ["loaiPet"] = "CHO", ["canNangKhaiBao"] = 5,
            ["dichVu"] = new JsonArray(Service(1))
        })
    };

    private static JsonObject Pet(JsonObject payload) => payload["thuCung"]![0]!.AsObject();

    private static JsonObject Service(int id) => new() { ["maDichVu"] = id, ["maKhungGio"] = 1 };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, JsonObject payload, byte[]? file = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), "payload");
        if (file is not null)
            form.Add(new ByteArrayContent(file), "vaccine", "vaccine.jpg");
        return await client.PostAsync("/api/bookings", form);
    }

    private static async Task<JsonNode> ReadDataAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.True(body["success"]!.GetValue<bool>());
            return body["data"]!;
        }
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.False(body["success"]!.GetValue<bool>());
            Assert.Equal(code, body["errorCode"]!.GetValue<string>());
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgresWebApplicationFactory _factory;
        public PostgresTestDatabase Database { get; }
        public RecordingStorage Storage { get; } = new();
        public RecordingEmail Email { get; }
        public HttpClient Client { get; }

        private Fixture(PostgresTestDatabase database, bool useRealStorage)
        {
            Database = database;
            Email = new RecordingEmail(database);
            _factory = new PostgresWebApplicationFactory(database.ConnectionString);
            Client = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                if (!useRealStorage)
                    services.AddSingleton<IFileStorageService>(Storage);
                services.AddSingleton<IEmailSender>(Email);
            })).CreateClient();
        }

        public static async Task<Fixture> CreateAsync(bool otp = false, bool useRealStorage = false)
        {
            var database = await PostgresTestDatabase.CreateAsync();
            try
            {
                await using var db = database.CreateContext();
                var employee = new NhanVien { Id = 1, TenDangNhap = "booking-staff", IsActive = true };
                var permission = new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "Care" };
                db.AddRange(employee, permission);
                db.NhanVienQuyenSet.Add(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "CARE_UPDATE", CapBoiId = 1 });
                db.CaLamViecSet.Add(new CaLamViec
                {
                    Id = 1, TenCa = "Sáng", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(12, 0),
                    IsActive = true, SucChuaPet = 1, TranPhut = 120
                });
                db.KhungGioDatLichSet.Add(new KhungGioDatLich
                {
                    Id = 1, Ngay = Date, CaLamViecId = 1, MoNhanKhach = true, TongSlot = 1, TranPhut = 120
                });
                db.DotDangKyCaSet.Add(new DotDangKyCa { Id = 1, TaoBoiId = 1, TuNgay = Date, DenNgay = Date,
                    MoAt = new DateTimeOffset(2036, 11, 1, 0, 0, 0, TimeSpan.Zero),
                    DongAt = new DateTimeOffset(2036, 11, 2, 0, 0, 0, TimeSpan.Zero) });
                db.DangKyCaSet.Add(new DangKyCa { Ngay = Date, CaLamViecId = 1, NhanVienId = 1, DotId = 1,
                    TrangThai = ShiftRegistrationState.APPROVED });
                db.CauHinhHeThongSet.Add(new CauHinhHeThong { Khoa = "EXTENSION_SETTINGS", SuaBoiId = 1,
                    PhienBan = 1, GiaTri = otp ? """{"otpEmail":true}""" : """{"otpEmail":false}""" });
                db.DichVuSet.AddRange(
                    new DichVu { Id = 1, TenDichVu = "Bath", LoaiDichVu = "SPA", ThoiLuongPhut = 60,
                        IsActive = true, YeuCauTiemChung = true },
                    new DichVu { Id = 2, TenDichVu = "Nails", LoaiDichVu = "SPA", ThoiLuongPhut = 30, IsActive = true });
                (decimal From, decimal? To)[] weights = [(0, 5), (5, 15), (15, 25), (25, null)];
                for (var index = 0; index < weights.Length; index++)
                {
                    var (from, to) = weights[index];
                    var sizeId = index + 1;
                    db.HeSoCanNangSet.Add(new HeSoCanNang { Id = sizeId, MaSize = (SizeBand)index,
                        CanNangTu = from, CanNangDen = to });
                    db.BangGiaSizeSet.AddRange(
                        new BangGiaSize { DichVuId = 1, SizeId = sizeId, DonGia = 100_000 * sizeId },
                        new BangGiaSize { DichVuId = 2, SizeId = sizeId, DonGia = 50_000 * sizeId });
                }
                await db.SaveChangesAsync();
                return new Fixture(database, useRealStorage);
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class RecordingStorage : IFileStorageService
    {
        private int _uploadCount;
        public int UploadCount => _uploadCount;
        public Func<int, CancellationToken, Task>? BeforeUpload { get; set; }
        public System.Collections.Concurrent.ConcurrentBag<string> Deleted { get; } = [];

        public async Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _uploadCount);
            if (BeforeUpload is not null)
                await BeforeUpload(attempt, cancellationToken);
            return $"test-{attempt}.jpg";
        }

        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Deleted.Add(objectPath);
            return Task.CompletedTask;
        }

        public Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("E04 must not create a read URL.");
    }

    private sealed class RecordingEmail(PostgresTestDatabase database) : IEmailSender
    {
        private int _attempts;
        public int Attempts => _attempts;
        public bool Fail { get; set; }
        public bool BookingWasCommitted { get; private set; }
        public Task? WaitFor { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("E04 must not send an approval email.");

        public async Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            await using var db = database.CreateContext();
            BookingWasCommitted = await db.DonDatLichSet.AnyAsync(
                booking => booking.EmailSnapshot == recipient && booking.TrackingCode == trackingCode, cancellationToken);
            Started.TrySetResult();
            if (WaitFor is not null)
                await WaitFor.WaitAsync(cancellationToken);
            if (Fail)
                throw new System.Net.Mail.SmtpException("Test delivery failure");
        }
    }
}
