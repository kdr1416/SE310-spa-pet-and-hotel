namespace PetSpa.Core.DTOs.Identity;

public class Session
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public Employee NhanVien { get; set; } = new();
}
