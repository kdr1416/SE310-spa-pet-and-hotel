using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class PhuPhi
{
    public int Id { get; set; }
    public string TenPhuPhi { get; set; } = string.Empty;
    public decimal SoTien { get; set; }
    public string LyDo { get; set; } = string.Empty;
    public DateTimeOffset? HuyAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DonDatLichId { get; set; }
    public int? ChiTietDonId { get; set; }
    public int TaoBoiId { get; set; }

    public DonDatLich DonDatLich { get; set; } = null!;
    public ChiTietDon? ChiTietDon { get; set; }
    public NhanVien TaoBoi { get; set; } = null!;
    public ICollection<CtHoaDon> CtHoaDonPhuPhiItems { get; set; } = new List<CtHoaDon>();
}
