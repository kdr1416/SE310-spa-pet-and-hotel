using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Care;

public class NhatKyChamSoc
{
    public int Id { get; set; }
    public string LoaiNhatKy { get; set; } = string.Empty;
    public DateTimeOffset GhiNhanAt { get; set; }
    public string? ObjectPath { get; set; }
    public string? GhiChu { get; set; }
    public decimal? CanNangGhiNhan { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int ChiTietDonId { get; set; }
    public int NhanVienId { get; set; }

    public ChiTietDon ChiTietDon { get; set; } = null!;
    public NhanVien NhanVien { get; set; } = null!;
}
