using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class DichVu
{
    public int Id { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string LoaiDichVu { get; set; } = string.Empty;
    public int ThoiLuongPhut { get; set; }
    public bool YeuCauTiemChung { get; set; }
    public bool IsActive { get; set; }
    public string? MoTa { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ChiTietCombo> ChiTietComboDichVuItems { get; set; } = new List<ChiTietCombo>();
    public ICollection<BangGiaSize> BangGiaSizeDichVuItems { get; set; } = new List<BangGiaSize>();
    public ICollection<DichVuTrongDon> DichVuTrongDonDichVuItems { get; set; } = new List<DichVuTrongDon>();
    public ICollection<ThanhPhanDichVuTrongDon> ThanhPhanDichVuTrongDonDichVuItems { get; set; } = new List<ThanhPhanDichVuTrongDon>();
}
