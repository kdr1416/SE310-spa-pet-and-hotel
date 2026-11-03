using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class NhanVien
{
    public int Id { get; set; }
    public string TenNhanVien { get; set; } = string.Empty;
    public string TenDangNhap { get; set; } = string.Empty;
    public string MatKhauHash { get; set; } = string.Empty;
    public Role VaiTro { get; set; }
    public bool IsActive { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<NhanVienQuyen> NhanVienQuyenNhanVienItems { get; set; } = new List<NhanVienQuyen>();
    public ICollection<NhanVienQuyen> NhanVienQuyenCapBoiItems { get; set; } = new List<NhanVienQuyen>();
    public ICollection<BaoVeTracking> BaoVeTrackingMoBoiItems { get; set; } = new List<BaoVeTracking>();
    public ICollection<AuditLog> AuditLogActorItems { get; set; } = new List<AuditLog>();
    public ICollection<DonDatLich> DonDatLichTaoBoiItems { get; set; } = new List<DonDatLich>();
    public ICollection<DonDatLich> DonDatLichDuyetBoiItems { get; set; } = new List<DonDatLich>();
    public ICollection<DonDatLich> DonDatLichHuyBoiItems { get; set; } = new List<DonDatLich>();
    public ICollection<ChiTietDon> ChiTietDonNhanVienItems { get; set; } = new List<ChiTietDon>();
    public ICollection<ChiTietDon> ChiTietDonXacMinhTiemBoiItems { get; set; } = new List<ChiTietDon>();
    public ICollection<ChiTietDon> ChiTietDonCheckInBoiItems { get; set; } = new List<ChiTietDon>();
    public ICollection<ChiTietDon> ChiTietDonBanGiaoBoiItems { get; set; } = new List<ChiTietDon>();
    public ICollection<ChiTietDon> ChiTietDonHuyBoiItems { get; set; } = new List<ChiTietDon>();
    public ICollection<ChiTietDon> ChiTietDonNhanSomBoiItems { get; set; } = new List<ChiTietDon>();
    public ICollection<NhatKyChamSoc> NhatKyChamSocNhanVienItems { get; set; } = new List<NhatKyChamSoc>();
    public ICollection<PhuPhi> PhuPhiTaoBoiItems { get; set; } = new List<PhuPhi>();
    public ICollection<HoaDon> HoaDonTaoBoiItems { get; set; } = new List<HoaDon>();
    public ICollection<HoaDon> HoaDonThuBoiItems { get; set; } = new List<HoaDon>();
    public ICollection<HoaDon> HoaDonThuHoiBoiItems { get; set; } = new List<HoaDon>();
    public ICollection<GiaoDichThu> GiaoDichThuXacNhanBoiItems { get; set; } = new List<GiaoDichThu>();
    public ICollection<HoanCoc> HoanCocTaoBoiItems { get; set; } = new List<HoanCoc>();
    public ICollection<HoanCoc> HoanCocHoanBoiItems { get; set; } = new List<HoanCoc>();
    public ICollection<DangKyCa> DangKyCaNhanVienItems { get; set; } = new List<DangKyCa>();
    public ICollection<DangKyCa> DangKyCaDuyetBoiItems { get; set; } = new List<DangKyCa>();
    public ICollection<NgayNghi> NgayNghiTaoBoiItems { get; set; } = new List<NgayNghi>();
    public ICollection<DotDangKyCa> DotDangKyCaTaoBoiItems { get; set; } = new List<DotDangKyCa>();
    public ICollection<CauHinhHeThong> CauHinhHeThongSuaBoiItems { get; set; } = new List<CauHinhHeThong>();
}
