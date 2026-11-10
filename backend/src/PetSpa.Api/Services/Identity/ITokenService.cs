using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Services.Identity;

public interface ITokenService
{
    (string Token, int ExpiresInSeconds) GenerateToken(NhanVien nhanVien);
}
