using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class CauHinhHeThong
{
    public int Id { get; set; }
    public string Khoa { get; set; } = string.Empty;
    public string GiaTri { get; set; } = "{}";
    public int PhienBan { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int SuaBoiId { get; set; }

    public NhanVien SuaBoi { get; set; } = null!;
}
