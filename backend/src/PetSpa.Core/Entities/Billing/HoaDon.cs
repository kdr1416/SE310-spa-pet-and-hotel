using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class HoaDon
{
    public int Id { get; set; }
    public string MaHoaDon { get; set; } = string.Empty;
    public InvoiceState TrangThai { get; set; }
    public decimal TongTien { get; set; }
    public decimal CocKhauTru { get; set; }
    public decimal ConPhaiTra { get; set; }
    public string KhachSnapshot { get; set; } = "{}";
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ThuHoiAt { get; set; }
    public string? LyDoThuHoi { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DonDatLichId { get; set; }
    public int? ThayTheHoaDonId { get; set; }
    public int TaoBoiId { get; set; }
    public int? ThuBoiId { get; set; }
    public int? ThuHoiBoiId { get; set; }

    public DonDatLich DonDatLich { get; set; } = null!;
    public HoaDon? ThayTheHoaDon { get; set; }
    public NhanVien TaoBoi { get; set; } = null!;
    public NhanVien? ThuBoi { get; set; }
    public NhanVien? ThuHoiBoi { get; set; }
    public HoaDon? HoaDonThayTheHoaDon { get; set; }
    public ICollection<CtHoaDon> CtHoaDonHoaDonItems { get; set; } = new List<CtHoaDon>();
    public ICollection<GiaoDichThu> GiaoDichThuHoaDonItems { get; set; } = new List<GiaoDichThu>();
}
