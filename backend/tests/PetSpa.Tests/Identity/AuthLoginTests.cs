using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Identity;

public class AuthLoginTests
{
    [Fact]
    public async Task Login_EmptyPayload_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Errors.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_NonExistentUser_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create();
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "unknown_user", MatKhau = "Password123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_InactiveUser_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                Id = 101,
                TenDangNhap = "locked_user",
                TenNhanVien = "Nhân Viên Khóa",
                VaiTro = Role.STAFF,
                IsActive = false
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "Password123@");
            db.NhanVienSet.Add(user);
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "locked_user", MatKhau = "Password123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                Id = 102,
                TenDangNhap = "staff1",
                TenNhanVien = "Nguyễn Văn Staff",
                VaiTro = Role.STAFF,
                IsActive = true
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "CorrectPassword123@");
            db.NhanVienSet.Add(user);
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "staff1", MatKhau = "WrongPassword123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithCryptographicallyValidJwtAndSession()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                Id = 1,
                TenDangNhap = "admin",
                TenNhanVien = "Quản trị viên",
                VaiTro = Role.ADMIN,
                IsActive = true
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "Admin@123");
            db.NhanVienSet.Add(user);

            var q1 = new Quyen { MaQuyen = "BOOKING_APPROVE", TenQuyen = "Duyệt đơn" };
            var q2 = new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "Cập nhật chăm sóc" };
            db.QuyenSet.AddRange(q1, q2);

            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen { NhanVienId = 1, MaQuyen = "BOOKING_APPROVE", CapBoiId = 1, CapAt = DateTimeOffset.UtcNow },
                new NhanVienQuyen { NhanVienId = 1, MaQuyen = "CARE_UPDATE", CapBoiId = 1, CapAt = DateTimeOffset.UtcNow }
            );
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "admin", MatKhau = "Admin@123" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Message.Should().Be("Đăng nhập thành công");
        body.Data.Should().NotBeNull();

        var session = body.Data!;
        session.ExpiresIn.Should().Be(28800);
        session.NhanVien.TenDangNhap.Should().Be("admin");
        session.NhanVien.TenNhanVien.Should().Be("Quản trị viên");
        session.NhanVien.VaiTro.Should().Be("ADMIN");
        session.NhanVien.DangHoatDong.Should().BeTrue();
        session.NhanVien.Quyen.Should().Contain(new[] { "BOOKING_APPROVE", "CARE_UPDATE" });

        // Cryptographic verification of returned JWT
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "PetSpaApi",
            ValidateAudience = true,
            ValidAudience = "PetSpaClient",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthApplicationFactory.TestJwtSecret)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(session.AccessToken, validationParams, out _);
        principal.FindFirst("username")?.Value.Should().Be("admin");
        principal.FindFirst("role")?.Value.Should().Be("ADMIN");
        principal.FindFirst("id")?.Value.Should().Be("1");
    }
}
