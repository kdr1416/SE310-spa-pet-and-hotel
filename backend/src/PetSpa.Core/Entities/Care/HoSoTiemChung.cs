using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Care;

public class HoSoTiemChung
{
    public int Id { get; set; }
    public string LoaiVaccine { get; set; } = string.Empty;
    public DateOnly NgayTiem { get; set; }
    public DateOnly? HieuLucDen { get; set; }
    public string? ObjectPath { get; set; }
    public string? CoSoCap { get; set; }
    public string? GhiChu { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int ThuCungId { get; set; }

    public ThuCung ThuCung { get; set; } = null!;
    public ICollection<ChiTietDon> ChiTietDonHoSoTiemChungItems { get; set; } = new List<ChiTietDon>();
}
