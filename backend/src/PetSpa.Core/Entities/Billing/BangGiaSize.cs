using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class BangGiaSize
{
    public int Id { get; set; }
    public decimal DonGia { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? DichVuId { get; set; }
    public int? ComboId { get; set; }
    public int SizeId { get; set; }

    public DichVu? DichVu { get; set; }
    public Combo? Combo { get; set; }
    public HeSoCanNang KhoangSize { get; set; } = null!;
}
