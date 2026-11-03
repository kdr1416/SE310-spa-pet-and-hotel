using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class NgayNghi
{
    public int Id { get; set; }
    public DateOnly Ngay { get; set; }
    public string LyDo { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int TaoBoiId { get; set; }

    public NhanVien TaoBoi { get; set; } = null!;
}
