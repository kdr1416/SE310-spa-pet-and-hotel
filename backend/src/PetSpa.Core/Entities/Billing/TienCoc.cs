using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class TienCoc
{
    public int Id { get; set; }
    public decimal CoSoTinhSnapshot { get; set; }
    public decimal TyLe { get; set; }
    public decimal SoTienYeuCau { get; set; }
    public DateTimeOffset HetHanAt { get; set; }
    public DepositRequestState TrangThaiYeuCau { get; set; }
    public DateTimeOffset? KetThucAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DonDatLichId { get; set; }

    public DonDatLich DonDatLich { get; set; } = null!;
    public ICollection<GiaoDichThu> GiaoDichThuTienCocItems { get; set; } = new List<GiaoDichThu>();
    public ICollection<HoanCoc> HoanCocTienCocItems { get; set; } = new List<HoanCoc>();
}
