namespace PetSpa.Core.DTOs.Identity;

public class Employee
{
    public int MaNhanVien { get; set; }
    public string TenNhanVien { get; set; } = string.Empty;
    public string TenDangNhap { get; set; } = string.Empty;
    public string VaiTro { get; set; } = string.Empty;
    public List<string> Quyen { get; set; } = new();
    public bool DangHoatDong { get; set; }
}
