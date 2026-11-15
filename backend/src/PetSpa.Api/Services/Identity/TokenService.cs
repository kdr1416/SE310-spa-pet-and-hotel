using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Services.Identity;

public class TokenService(IConfiguration configuration) : ITokenService
{
    public (string Token, int ExpiresInSeconds) GenerateToken(NhanVien nhanVien)
    {
        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "PetSpaApi";
        var audience = configuration["Jwt:Audience"] ?? "PetSpaClient";
        var expiryMinutesStr = configuration["Jwt:ExpiryMinutes"];
        var expiryMinutes = int.TryParse(expiryMinutesStr, out var m) ? m : 480;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("id", nhanVien.Id.ToString()),
            new Claim("username", nhanVien.TenDangNhap),
            new Claim("role", nhanVien.VaiTro.ToString()),
            new Claim("jti", Guid.NewGuid().ToString())
        };

        var expires = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (tokenString, expiryMinutes * 60);
    }
}
