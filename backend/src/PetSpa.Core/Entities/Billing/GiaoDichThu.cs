using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class GiaoDichThu
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public decimal SoTien { get; set; }
    public PaymentMethod PhuongThuc { get; set; }
    public string? NganHang { get; set; }
    public string? MaGiaoDich { get; set; }
    public DateTimeOffset GiaoDichAt { get; set; }
    public DateTimeOffset XacNhanAt { get; set; }
    public string? GhiChu { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? TienCocId { get; set; }
    public int? HoaDonId { get; set; }
    public int XacNhanBoiId { get; set; }

    public TienCoc? TienCoc { get; set; }
    public HoaDon? HoaDon { get; set; }
    public NhanVien XacNhanBoi { get; set; } = null!;
}
