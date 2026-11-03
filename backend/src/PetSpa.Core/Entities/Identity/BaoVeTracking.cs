using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class BaoVeTracking
{
    public int Id { get; set; }
    public string KhoaIpHash { get; set; } = string.Empty;
    public string KhoaSdtHash { get; set; } = string.Empty;
    public int SoLanSai { get; set; }
    public DateTimeOffset BatDauCuaSo { get; set; }
    public DateTimeOffset? KhoaDen { get; set; }
    public DateTimeOffset? MoAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? MoBoiId { get; set; }

    public NhanVien? MoBoi { get; set; }
}
