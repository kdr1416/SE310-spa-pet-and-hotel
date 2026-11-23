using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using PetSpa.Api.Services.Identity;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Enums;
using PetSpa.Tests.Foundation;

namespace PetSpa.Tests.Care;

public class CareTasksEndpointTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2035-01-01T02:00:00Z");

    [Fact]
    public async Task SignsOnlyAuthorizedPageAndNeverReturnsPrivatePaths()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        await using var db = f.Database.CreateContext();
        var staff = await db.NhanVienSet.Where(x => x.VaiTro == Role.STAFF)
            .OrderByDescending(x => x.NhanVienQuyenNhanVienItems.Any(grant => grant.MaQuyen == "CARE_UPDATE")).ThenBy(x => x.Id).ToListAsync();
        for (var i = 0; i < 3; i++)
        {
            var booking = await f.AddBookingAsync(db, deposit: false);
            booking.TrangThai = BookingState.IN_PROGRESS;
            var pet = booking.ChiTietDonDonDatLichItems.Single();
            pet.NhanVienId = staff[i == 2 ? 1 : 0].Id;
            pet.TrangThaiChamSoc = PetCareState.WAITING;
            pet.NhatKyChamSocChiTietDonItems.Add(new NhatKyChamSoc {
                NhanVienId = staff[0].Id, ObjectPath = $"private-{i}.jpg", LoaiNhatKy = "PHOTO",
                GhiNhanAt = Now
            });
            await db.SaveChangesAsync();
        }
        var storage = new RecordingStorage();
        await using var host = f.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IFileStorageService>(storage))));
        using var scope = host.Services.CreateScope();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(staff[0]).Token);
        var response = await client.GetAsync("/api/internal/care-tasks?trang=2&kichThuocTrang=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.Paths.Should().Equal("private-1.jpg");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("objectPath").And.NotContain("private-0").And.NotContain("private-2");
        body.Should().Contain("https://media.example.test/signed");
    }

    private sealed class RecordingStorage : IFileStorageService
    {
        public List<string> Paths { get; } = [];
        public Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Paths.Add(objectPath);
            return Task.FromResult(new Uri("https://media.example.test/signed"));
        }
    }

    [Fact]
    public async Task StaffSeesOnlyCurrentAssignmentsAndCountBeforePagingWhileAdminSeesAll()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        await using var db = f.Database.CreateContext();
        var staff = await db.NhanVienSet.Where(x => x.VaiTro == Role.STAFF)
            .OrderByDescending(x => x.NhanVienQuyenNhanVienItems.Any(grant => grant.MaQuyen == "CARE_UPDATE")).ThenBy(x => x.Id).ToListAsync();
        var slot = await db.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync();
        slot.TranPhut = 1000;
        slot.TongSlot = 20;
        await db.SaveChangesAsync();
        var expected = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            var booking = await f.AddBookingAsync(db, deposit: false);
            booking.TrangThai = i == 7 ? BookingState.CONFIRMED : BookingState.IN_PROGRESS;
            var pet = booking.ChiTietDonDonDatLichItems.Single();
            pet.NhanVienId = staff[i == 6 ? 1 : 0].Id;
            pet.TrangThaiChamSoc = i < 4 ? (PetCareState)(i + 1) : i == 4 ? PetCareState.DONE : PetCareState.WAITING;
            if (i == 5) pet.TrangThaiChamSoc = PetCareState.NOT_ARRIVED;
            await db.SaveChangesAsync();
            if (i < 4) expected.Add(pet.Id);
        }
        using var scope = f.Host.Services.CreateScope();
        using var client = f.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(staff[0]).Token);
        var response = await client.GetAsync("/api/internal/care-tasks?trang=2&kichThuocTrang=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("tongSo").GetInt32().Should().Be(4);
        data.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("maChiTietDon").GetInt32())
            .Should().Equal(expected.Skip(2));
        using var admin = await f.AdminClientAsync();
        using var all = JsonDocument.Parse(await admin.GetStringAsync("/api/internal/care-tasks"));
        all.RootElement.GetProperty("data").GetProperty("tongSo").GetInt32().Should().Be(5);
        using var ready = JsonDocument.Parse(await client.GetStringAsync("/api/internal/care-tasks?trangThai=READY"));
        ready.RootElement.GetProperty("data").GetProperty("tongSo").GetInt32().Should().Be(1);
        using var empty = JsonDocument.Parse(await client.GetStringAsync("/api/internal/care-tasks?tuNgay=2035-01-02"));
        empty.RootElement.GetProperty("data").GetProperty("tongSo").GetInt32().Should().Be(0);
        await db.NhanVienQuyenSet.Where(x => x.NhanVienId == staff[0].Id && x.MaQuyen == "CARE_UPDATE").ExecuteDeleteAsync();
        (await client.GetAsync("/api/internal/care-tasks")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("trangThai=DONE")]
    [InlineData("trangThai=1")]
    [InlineData("tuNgay=2035-02-01&denNgay=2035-01-01")]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-05-01")]
    [InlineData("kichThuocTrang=101")]
    [InlineData("trang=0")]
    public async Task InvalidQueryIsRejected(string query)
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        using var client = await f.AdminClientAsync();
        (await client.GetAsync("/api/internal/care-tasks?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnonymousCannotReadTasks()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        using var client = f.Host.CreateClient();
        (await client.GetAsync("/api/internal/care-tasks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
