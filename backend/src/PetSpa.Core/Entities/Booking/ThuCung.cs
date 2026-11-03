using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Booking;

public class ThuCung
{
    public int Id { get; set; }
    public string TenThuCung { get; set; } = string.Empty;
    public PetType LoaiPet { get; set; }
    public decimal CanNang { get; set; }
    public string? Giong { get; set; }
    public DateOnly? NgaySinh { get; set; }
    public string? LoaiLong { get; set; }
    public string? GhiChuDiUng { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int KhachHangId { get; set; }

    public KhachHang KhachHang { get; set; } = null!;
    public ICollection<ChiTietDon> ChiTietDonThuCungItems { get; set; } = new List<ChiTietDon>();
    public ICollection<HoSoTiemChung> HoSoTiemChungThuCungItems { get; set; } = new List<HoSoTiemChung>();
}
