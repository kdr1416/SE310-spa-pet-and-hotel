using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class Combo
{
    public int Id { get; set; }
    public string TenCombo { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? MoTa { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ChiTietCombo> ChiTietComboComboItems { get; set; } = new List<ChiTietCombo>();
    public ICollection<BangGiaSize> BangGiaSizeComboItems { get; set; } = new List<BangGiaSize>();
    public ICollection<DichVuTrongDon> DichVuTrongDonComboItems { get; set; } = new List<DichVuTrongDon>();
}
