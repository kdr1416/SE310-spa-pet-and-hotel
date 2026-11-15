using Microsoft.AspNetCore.Mvc ;
using PetSpa.Core.Common ;
using PetSpa.Core.DTOs.Availability ;
using PetSpa.Core.Services ;

namespace PetSpa.Api.Controllers ;
[ApiController]
[Route("api/availability")]
public class AvailabilityController : ControllerBase {
    private readonly IAvailabilityService _availabilityService ;
     // Dependency Injection : Nhận IAvailability từ hệ thông
     public AvailabilityController(IAvailabilityService availabilityService){
        _availabilityService = availabilityService ;
     }
     [HttpPost]
     public async Task<IActionResult> CheckAvailability(
        [FromBody] AvailabilityQuery query ,
        CancellationToken cancellationToken )
     {
        //1. Gọi Service để kiểm tra
        var result = await _availabilityService.CheckAvailabilityAsync(query,cancellationToken) ;
        //2. Nếu ca không đủ chỗ hoặc vi phạm quy tắc: trả HTTP 409 theo DoD
        if (!result.CoTheDat)
        {
            var message = result.LyDo ?? "Không đủ công suất ca đã chọn." ;
            return Conflict(ApiResponse<AvailabilityResponse>.Fail(
                message: message,
                errorCode :"CAPACITY_EXCEEDED"
            )) ;
        }
        //3. Nếu thành công :trả HTTP 200 OK
        return Ok(ApiResponse<AvailabilityResponse>.Ok(
            data:result ,
            message :"Ca làm việc còn khả dụng. "
        )) ;
     }
}