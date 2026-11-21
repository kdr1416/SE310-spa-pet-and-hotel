using System.Net;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using System.Net.Http.Json ;
using System.Text.Json ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc.Testing ;
using Microsoft.AspNetCore.TestHost ;
using Microsoft.Extensions.DependencyInjection ;
using PetSpa.Core.Common ;
using PetSpa.Core.DTOs.Availability ;
using PetSpa.Core.Services ;
using Xunit ;

namespace PetSpa.Tests ;

public class AvailabilityEndpointTests :
IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory ;

    public AvailabilityEndpointTests (WebApplicationFactory<Program> factory)
    {
        _factory = factory ;
    }
    //1. DoD Happy Path :Ca còn đủ chỗ -> trả HTTP 200 OK với coTheDat = true
    [Fact]
    public async Task CheckAvailability_WhenSlotAvailable_RerurnsOk_WithCoTheDatTrue()
    {
        // Arrange: Giả lập Service trả về kết quả còn chỗ 
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IAvailabilityService>(_=>new FakeAvailabilityService(new AvailabilityResponse{
                    MaKhungGio = 1 ,
                    SlotConLai = 5 ,
                    PhutConLai = 120 ,
                    CoTheDat = true 
                })) ;
            }) ;
        }
        ).CreateClient() ;
        var query = new AvailabilityQuery{
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026,11,20) ,
            ThuCung = new List<PetDemand> 
            {
                new() {DichVu = new List<ServiceSelection> {
                    new() {MaDichVu = 1 ,MaKhungGio =1}
                }}}
            };
            
            // Act : Bắn request POST/api/availability
            var response = await client.PostAsJsonAsync("/api/availability",query) ;
            //Assert : Kiểm tra HTTP status và nội dung JSON
            response.StatusCode.Should().Be(HttpStatusCode.OK) ;

            var content = await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>() ;
            content.Should().NotBeNull() ;
            content!.Success.Should().BeTrue() ;
            content.Data!.CoTheDat.Should().BeTrue() ;
            content.Data.SlotConLai.Should().Be(5) ;
    }
    
    // 2.DoD Sad Path: Ca hết chỗ/hết phút -> trả HTTP 409 với CAPACITY_EXCEEDED
     [Fact]
        public async Task CheckAVailability_WhenCapacityExceeded_Returns409Conflict_WithErrorCode() {
            //Arrange :Gỉa lập Service báo hết chỗ 
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped<IAvailabilityService>(_=>new FakeAvailabilityService(new AvailabilityResponse
                    {
                        MaKhungGio = 1,
                        SlotConLai = 0,
                        PhutConLai = 0,
                        CoTheDat = false,
                        LyDo = "Ca làm việc đã hết chỗ chứa thú cưng."
                    })) ;
                });
            }).CreateClient() ;
            var query = new AvailabilityQuery{
                MaKhungGio = 1,
                NgayTra = new DateOnly(2026,11,20),
                ThuCung =  new List<PetDemand>
                {
                    new() {DichVu =new List<ServiceSelection>{
                        new(){MaDichVu = 1, MaKhungGio = 1
                    }}
                    }
                }
            };
            // Act: Bắn request POST /api/availability
            var response = await client.PostAsJsonAsync("api/availability",query) ;
            // Assert: Kỳ vọng HTTP 409 Cònlict và mã CAPACITY_EXCEEDED
            response.StatusCode.Should().Be(HttpStatusCode.Conflict) ;

            var content = await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>() ;
            content.Should().NotBeNull() ;
            content!.Success.Should().BeFalse() ;
            content.ErrorCode.Should().Be("CAPACITY_EXCEEDED") ;
            content.Message.Should().Contain("hết chỗ") ;
        }
    [Theory]
    [InlineData("valid", HttpStatusCode.OK, null)]
    [InlineData("missing-slot", HttpStatusCode.NotFound, "NOT_FOUND")]
    [InlineData("room", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("combo", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("unknown-service", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("inactive-service", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("combo-only-service", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("legacy-grooming", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("unknown-service-slot", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("overnight", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    [InlineData("disabled-staff", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("no-grant", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("minute-limit", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("pet-limit", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("closed", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    [InlineData("holiday", HttpStatusCode.Conflict, "CAPACITY_EXCEEDED")]
    public async Task Availability_ValidatesSpaDemandAndUsesSlotLimits(string scenario, HttpStatusCode status, string? code)
    {
        var date = new DateOnly(2026, 11, 20);
        using var factory = AuthApplicationFactory.Create(db =>
        {
            db.NhanVienSet.Add(new NhanVien { Id = 1, IsActive = scenario != "disabled-staff", TenDangNhap = "availability-staff" });
            if (scenario != "no-grant")
                db.NhanVienQuyenSet.Add(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "CARE_UPDATE", CapBoiId = 1 });
            db.CaLamViecSet.Add(new CaLamViec
            {
                Id = 1, IsActive = true, GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(12, 0),
                SucChuaPet = 10, TranPhut = 240
            });
            db.KhungGioDatLichSet.Add(new KhungGioDatLich
            {
                Id = 1, Ngay = date, CaLamViecId = 1, TongSlot = 2, SlotDaDat = scenario == "pet-limit" ? 2 : 1,
                TranPhut = 70, PhutDaGiu = scenario == "minute-limit" ? 11 : 0, MoNhanKhach = scenario != "closed"
            });
            db.DangKyCaSet.Add(new DangKyCa { Ngay = date, CaLamViecId = 1, NhanVienId = 1, TrangThai = ShiftRegistrationState.APPROVED });
            db.DichVuSet.Add(new DichVu
            {
                Id = 1, IsActive = scenario != "inactive-service",
                TenDichVu = scenario == "legacy-grooming" ? "Cắt tỉa tạo kiểu" : "Tắm sấy",
                LoaiDichVu = scenario == "combo-only-service" ? "SPA_ADVANCED" : "SPA", ThoiLuongPhut = 60
            });
            if (scenario == "holiday")
                db.NgayNghiSet.Add(new NgayNghi { Ngay = date });
            db.SaveChanges();
        });
        using var client = factory.CreateClient();
        var bookingSlot = scenario == "missing-slot" ? 99 : 1;
        using var response = await client.PostAsJsonAsync("/api/availability", new AvailabilityQuery
        {
            MaKhungGio = bookingSlot,
            NgayTra = scenario == "overnight" ? date.AddDays(1) : date,
            ThuCung = [new PetDemand
            {
                MaLoaiPhong = scenario == "room" ? 1 : null,
                DichVu = [new ServiceSelection
                {
                    MaCombo = scenario == "combo" ? 1 : null,
                    MaDichVu = scenario == "combo" ? null : scenario == "unknown-service" ? 99 : 1,
                    MaKhungGio = scenario == "unknown-service-slot" ? 99 : bookingSlot
                }]
            }]
        });
        Assert.Equal(status, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>())!;
        Assert.Equal(code, body.ErrorCode);
        if (status == HttpStatusCode.OK)
        {
            Assert.Equal(1, body.Data!.SlotConLai);
            Assert.Equal(70, body.Data.PhutConLai);
        }
        else
        {
            Assert.NotEmpty(body.TraceId!);
        }
    }

    // Class giả lập Service phục vụ  cho việc test độc lập(Stub/Fake)
    private class FakeAvailabilityService : IAvailabilityService
    {
      private readonly AvailabilityResponse _mockResponse ;
      public FakeAvailabilityService(AvailabilityResponse mockResponse) => _mockResponse = mockResponse ;
      public Task<AvailabilityCheck> CheckAvailabilityAsync(
        AvailabilityQuery query, CancellationToken  cancellationToken = default
      )
      {
        return Task.FromResult(_mockResponse.CoTheDat ? new AvailabilityCheck(_mockResponse) : new AvailabilityCheck(null, "CAPACITY_EXCEEDED", _mockResponse.LyDo));
      }
    }
 
 
}
