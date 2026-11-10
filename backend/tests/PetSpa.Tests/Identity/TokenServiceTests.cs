using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using Xunit;

namespace PetSpa.Tests.Identity;

public class TokenServiceTests
{
    [Fact]
    public void GenerateToken_ProducesValidCryptographicJwt_WithExpectedClaimsAndExpiry()
    {
        // Arrange
        const string secretKey = "test_secret_key_at_least_32_bytes_long_entropy_123456789!";
        const string issuer = "PetSpaTestIssuer";
        const string audience = "PetSpaTestAudience";
        const int expiryMinutes = 480;

        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = secretKey,
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        ITokenService tokenService = new TokenService(configuration);

        var nhanVien = new NhanVien
        {
            Id = 42,
            TenDangNhap = "staff_test",
            TenNhanVien = "Nguyễn Văn Test",
            VaiTro = Role.STAFF,
            IsActive = true
        };

        // Act
        var (token, expiresIn) = tokenService.GenerateToken(nhanVien);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(expiryMinutes * 60);

        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.CanReadToken(token).Should().BeTrue();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(token, validationParams, out var validatedToken);
        principal.FindFirst("id")?.Value.Should().Be("42");
        principal.FindFirst("username")?.Value.Should().Be("staff_test");
        principal.FindFirst("role")?.Value.Should().Be("STAFF");
        principal.FindFirst("jti")?.Value.Should().NotBeNullOrWhiteSpace();
    }
}
