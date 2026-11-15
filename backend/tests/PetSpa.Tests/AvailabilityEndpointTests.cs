using System.Net ;
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
    // Class giả lập Service phục vụ  cho việc test độc lập(Stub/Fake)
    private class FakeAvailabilityService : IAvailabilityService
    {
      private readonly AvailabilityResponse _mockResponse ;
      public FakeAvailabilityService(AvailabilityResponse mockResponse) => _mockResponse = mockResponse ;
      public Task<AvailabilityResponse> CheckAvailabilityAsync(
        AvailabilityQuery query, CancellationToken  cancellationToken = default
      )
      {
        return Task.FromResult(_mockResponse) ;
      }
    }
 
 
}
