using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class CtHoaDon
{
    public int Id { get; set; }
    public string LoaiKhoan { get; set; } = string.Empty;
    public string? TenPetSnapshot { get; set; }
    public string TenKhoanMuc { get; set; } = string.Empty;
    public SizeBand? SizeSnapshot { get; set; }
    public int SoLuong { get; set; }
    public decimal DonGiaSnapshot { get; set; }
    public decimal ThanhTien { get; set; }
    public string? DienGiai { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int HoaDonId { get; set; }
    public int? ChiTietDonId { get; set; }
    public int? DichVuTrongDonId { get; set; }
    public int? PhuPhiId { get; set; }

    public HoaDon HoaDon { get; set; } = null!;
    public ChiTietDon? ChiTietDon { get; set; }
    public DichVuTrongDon? DichVuTrongDon { get; set; }
    public PhuPhi? PhuPhi { get; set; }
}
