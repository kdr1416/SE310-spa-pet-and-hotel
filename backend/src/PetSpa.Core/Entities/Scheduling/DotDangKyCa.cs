using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class DotDangKyCa
{
    public int Id { get; set; }
    public DateOnly TuNgay { get; set; }
    public DateOnly DenNgay { get; set; }
    public DateTimeOffset MoAt { get; set; }
    public DateTimeOffset DongAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int TaoBoiId { get; set; }

    public NhanVien TaoBoi { get; set; } = null!;
    public ICollection<DangKyCa> DangKyCaDotItems { get; set; } = new List<DangKyCa>();
}
