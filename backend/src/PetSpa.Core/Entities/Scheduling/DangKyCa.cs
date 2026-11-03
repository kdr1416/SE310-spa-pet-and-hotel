using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class DangKyCa
{
    public int Id { get; set; }
    public DateOnly Ngay { get; set; }
    public ShiftRegistrationState TrangThai { get; set; }
    public DateTimeOffset? DuyetAt { get; set; }
    public string? LyDoDieuChuyen { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int NhanVienId { get; set; }
    public int CaLamViecId { get; set; }
    public int DotId { get; set; }
    public int? DuyetBoiId { get; set; }

    public NhanVien NhanVien { get; set; } = null!;
    public CaLamViec CaLamViec { get; set; } = null!;
    public DotDangKyCa Dot { get; set; } = null!;
    public NhanVien? DuyetBoi { get; set; }
}
