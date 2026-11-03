using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class LoaiPhong
{
    public int Id { get; set; }
    public string TenPhong { get; set; } = string.Empty;
    public decimal GiaMoiDem { get; set; }
    public int SoLuongPhong { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ChiTietDon> ChiTietDonLoaiPhongItems { get; set; } = new List<ChiTietDon>();
}
