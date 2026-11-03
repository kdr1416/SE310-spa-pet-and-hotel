using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class HeSoCanNang
{
    public int Id { get; set; }
    public SizeBand MaSize { get; set; }
    public decimal CanNangTu { get; set; }
    public decimal? CanNangDen { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<BangGiaSize> BangGiaSizeKhoangSizeItems { get; set; } = new List<BangGiaSize>();
}
