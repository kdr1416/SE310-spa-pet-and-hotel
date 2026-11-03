using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class NhanVienQuyen
{
    public DateTimeOffset CapAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int NhanVienId { get; set; }
    public string MaQuyen { get; set; } = string.Empty;
    public int CapBoiId { get; set; }

    public NhanVien NhanVien { get; set; } = null!;
    public Quyen Quyen { get; set; } = null!;
    public NhanVien CapBoi { get; set; } = null!;
}
