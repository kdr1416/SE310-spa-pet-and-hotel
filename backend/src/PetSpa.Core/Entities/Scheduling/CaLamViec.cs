using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class CaLamViec
{
    public int Id { get; set; }
    public string TenCa { get; set; } = string.Empty;
    public TimeOnly GioBatDau { get; set; }
    public TimeOnly GioKetThuc { get; set; }
    public int SucChuaPet { get; set; }
    public int TranPhut { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<KhungGioDatLich> KhungGioDatLichCaLamViecItems { get; set; } = new List<KhungGioDatLich>();
    public ICollection<DangKyCa> DangKyCaCaLamViecItems { get; set; } = new List<DangKyCa>();
}
