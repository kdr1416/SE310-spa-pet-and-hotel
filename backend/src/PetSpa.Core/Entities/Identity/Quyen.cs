using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class Quyen
{
    public string MaQuyen { get; set; } = string.Empty;
    public string TenQuyen { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<NhanVienQuyen> NhanVienQuyenQuyenItems { get; set; } = new List<NhanVienQuyen>();
}
