using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class ChiTietCombo
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int ComboId { get; set; }
    public int DichVuId { get; set; }

    public Combo Combo { get; set; } = null!;
    public DichVu DichVu { get; set; } = null!;
}
