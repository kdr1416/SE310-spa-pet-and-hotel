using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Booking;

public class KhachHang
{
    public int Id { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string SoDienThoai { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<OtpChallenge> OtpChallengeKhachHangItems { get; set; } = new List<OtpChallenge>();
    public ICollection<ThuCung> ThuCungKhachHangItems { get; set; } = new List<ThuCung>();
    public ICollection<DonDatLich> DonDatLichKhachHangItems { get; set; } = new List<DonDatLich>();
    public ICollection<DanhGia> DanhGiaKhachHangItems { get; set; } = new List<DanhGia>();
}
