using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Care;

public class DanhGia
{
    public int Id { get; set; }
    public int SoSao { get; set; }
    public string? BinhLuan { get; set; }
    public string? AnhPaths { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DonDatLichId { get; set; }
    public int KhachHangId { get; set; }
    public int? ChiTietDonId { get; set; }
    public int? DichVuTrongDonId { get; set; }

    public DonDatLich DonDatLich { get; set; } = null!;
    public KhachHang KhachHang { get; set; } = null!;
    public ChiTietDon? ChiTietDon { get; set; }
    public DichVuTrongDon? DichVuTrongDon { get; set; }
}
