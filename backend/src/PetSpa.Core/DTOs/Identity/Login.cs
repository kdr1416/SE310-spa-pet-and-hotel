using System.ComponentModel.DataAnnotations;

namespace PetSpa.Core.DTOs.Identity;

public class Login
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [MaxLength(50, ErrorMessage = "Tên đăng nhập tối đa 50 ký tự.")]
    public string TenDangNhap { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [MaxLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    public string MatKhau { get; set; } = string.Empty;
}
