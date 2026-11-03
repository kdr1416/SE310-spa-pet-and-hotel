using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Booking;

public class DonDatLich
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string TrackingCode { get; set; } = string.Empty;
    public BookingState TrangThai { get; set; }
    public string TenNguoiDat { get; set; } = string.Empty;
    public string SoDienThoaiSnapshot { get; set; } = string.Empty;
    public string EmailSnapshot { get; set; } = string.Empty;
    public DateTimeOffset BatDauDuKien { get; set; }
    public DateTimeOffset KetThucDuKien { get; set; }
    public DateTimeOffset MocCuoiCa { get; set; }
    public decimal TongTamTinh { get; set; }
    public string ChinhSachSnapshot { get; set; } = "{}";
    public string? EmailDelivery { get; set; }
    public DateTimeOffset? EmailAttemptAt { get; set; }
    public DateTimeOffset? DuyetAt { get; set; }
    public DateTimeOffset? HuyAt { get; set; }
    public DateTimeOffset? HoanTatAt { get; set; }
    public CancellationType? LoaiHuy { get; set; }
    public string? LyDoHuy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int KhachHangId { get; set; }
    public int KhungGioId { get; set; }
    public int? TaoBoiId { get; set; }
    public int? DuyetBoiId { get; set; }
    public int? HuyBoiId { get; set; }

    public KhachHang KhachHang { get; set; } = null!;
    public KhungGioDatLich KhungGio { get; set; } = null!;
    public NhanVien? TaoBoi { get; set; }
    public NhanVien? DuyetBoi { get; set; }
    public NhanVien? HuyBoi { get; set; }
    public ICollection<ChiTietDon> ChiTietDonDonDatLichItems { get; set; } = new List<ChiTietDon>();
    public DanhGia? DanhGiaDonDatLich { get; set; }
    public ICollection<PhuPhi> PhuPhiDonDatLichItems { get; set; } = new List<PhuPhi>();
    public ICollection<HoaDon> HoaDonDonDatLichItems { get; set; } = new List<HoaDon>();
    public TienCoc? TienCocDonDatLich { get; set; }
}
