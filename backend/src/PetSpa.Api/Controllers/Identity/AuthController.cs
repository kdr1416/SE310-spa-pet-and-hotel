using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Controllers.Identity;

[ApiController]
[Route("api/auth")]
public class AuthController(PetSpaDbContext dbContext, ITokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<Session>>> Login([FromBody] Login request)
    {
        var nhanVien = await dbContext.NhanVienSet
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.TenDangNhap == request.TenDangNhap);

        if (nhanVien == null || !nhanVien.IsActive)
        {
            return BadRequest(ApiResponse<Session>.Fail(
                "Tên đăng nhập hoặc mật khẩu không chính xác.",
                "VALIDATION_FAILED",
                null,
                HttpContext.TraceIdentifier));
        }

        var hasher = new PasswordHasher<NhanVien>();
        var verifyResult = hasher.VerifyHashedPassword(nhanVien, nhanVien.MatKhauHash, request.MatKhau);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return BadRequest(ApiResponse<Session>.Fail(
                "Tên đăng nhập hoặc mật khẩu không chính xác.",
                "VALIDATION_FAILED",
                null,
                HttpContext.TraceIdentifier));
        }

        var (token, expiresIn) = tokenService.GenerateToken(nhanVien);

        var session = new Session
        {
            AccessToken = token,
            ExpiresIn = expiresIn,
            NhanVien = new Employee
            {
                MaNhanVien = nhanVien.Id,
                TenNhanVien = nhanVien.TenNhanVien,
                TenDangNhap = nhanVien.TenDangNhap,
                VaiTro = nhanVien.VaiTro.ToString(),
                Quyen = nhanVien.NhanVienQuyenNhanVienItems.Select(q => q.MaQuyen).ToList(),
                DangHoatDong = nhanVien.IsActive
            }
        };

        return Ok(ApiResponse<Session>.Ok(session, "Đăng nhập thành công"));
    }
}
