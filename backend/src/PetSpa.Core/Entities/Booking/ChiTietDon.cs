using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Booking;

public class ChiTietDon
{
    public int Id { get; set; }
    public string TenPetSnapshot { get; set; } = string.Empty;
    public PetCareState TrangThaiChamSoc { get; set; }
    public decimal? CanNangChot { get; set; }
    public SizeBand? SizeThucTe { get; set; }
    public SizeBand? SizeChot { get; set; }
    public string? ThoaThuanGia { get; set; }
    public decimal? GiaPhongCamKet { get; set; }
    public decimal? GiaPhongChot { get; set; }
    public int? SoDemChot { get; set; }
    public DateTimeOffset? CheckInAt { get; set; }
    public DateTimeOffset? BanGiaoAt { get; set; }
    public DateTimeOffset? TraLaiAt { get; set; }
    public bool NhanSom { get; set; }
    public FurCondition? TinhTrangLong { get; set; }
    public string? GhiChuSucKhoe { get; set; }
    public DateTimeOffset? XacMinhTiemAt { get; set; }
    public DateTimeOffset? HuyAt { get; set; }
    public string? LyDoHuy { get; set; }
    public int? SoDemDaO { get; set; }
    public string? NhanSomLyDo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DonDatLichId { get; set; }
    public int ThuCungId { get; set; }
    public int? LoaiPhongId { get; set; }
    public int? NhanVienId { get; set; }
    public int? HoSoTiemChungId { get; set; }
    public int? XacMinhTiemBoiId { get; set; }
    public int? CheckInBoiId { get; set; }
    public int? BanGiaoBoiId { get; set; }
    public int? HuyBoiId { get; set; }
    public int? NhanSomBoiId { get; set; }
    public decimal CanNangKhaiBao { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }

    public DonDatLich DonDatLich { get; set; } = null!;
    public ThuCung ThuCung { get; set; } = null!;
    public LoaiPhong? LoaiPhong { get; set; }
    public NhanVien? NhanVien { get; set; }
    public HoSoTiemChung? HoSoTiemChung { get; set; }
    public NhanVien? XacMinhTiemBoi { get; set; }
    public NhanVien? CheckInBoi { get; set; }
    public NhanVien? BanGiaoBoi { get; set; }
    public NhanVien? HuyBoi { get; set; }
    public NhanVien? NhanSomBoi { get; set; }
    public ICollection<NhatKyChamSoc> NhatKyChamSocChiTietDonItems { get; set; } = new List<NhatKyChamSoc>();
    public ICollection<DanhGia> DanhGiaChiTietDonItems { get; set; } = new List<DanhGia>();
    public ICollection<DichVuTrongDon> DichVuTrongDonChiTietDonItems { get; set; } = new List<DichVuTrongDon>();
    public ICollection<PhuPhi> PhuPhiChiTietDonItems { get; set; } = new List<PhuPhi>();
    public ICollection<CtHoaDon> CtHoaDonChiTietDonItems { get; set; } = new List<CtHoaDon>();
}
