using Microsoft.EntityFrameworkCore;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data;

public sealed class PetSpaDbContext(DbContextOptions<PetSpaDbContext> options) : DbContext(options)
{
    public DbSet<NhanVien> NhanVienSet => Set<NhanVien>();
    public DbSet<Quyen> QuyenSet => Set<Quyen>();
    public DbSet<NhanVienQuyen> NhanVienQuyenSet => Set<NhanVienQuyen>();
    public DbSet<OtpChallenge> OtpChallengeSet => Set<OtpChallenge>();
    public DbSet<BaoVeTracking> BaoVeTrackingSet => Set<BaoVeTracking>();
    public DbSet<AuditLog> AuditLogSet => Set<AuditLog>();
    public DbSet<KhachHang> KhachHangSet => Set<KhachHang>();
    public DbSet<ThuCung> ThuCungSet => Set<ThuCung>();
    public DbSet<DonDatLich> DonDatLichSet => Set<DonDatLich>();
    public DbSet<ChiTietDon> ChiTietDonSet => Set<ChiTietDon>();
    public DbSet<HoSoTiemChung> HoSoTiemChungSet => Set<HoSoTiemChung>();
    public DbSet<NhatKyChamSoc> NhatKyChamSocSet => Set<NhatKyChamSoc>();
    public DbSet<DanhGia> DanhGiaSet => Set<DanhGia>();
    public DbSet<DichVu> DichVuSet => Set<DichVu>();
    public DbSet<Combo> ComboSet => Set<Combo>();
    public DbSet<ChiTietCombo> ChiTietComboSet => Set<ChiTietCombo>();
    public DbSet<BangGiaSize> BangGiaSizeSet => Set<BangGiaSize>();
    public DbSet<HeSoCanNang> HeSoCanNangSet => Set<HeSoCanNang>();
    public DbSet<LoaiPhong> LoaiPhongSet => Set<LoaiPhong>();
    public DbSet<DichVuTrongDon> DichVuTrongDonSet => Set<DichVuTrongDon>();
    public DbSet<ThanhPhanDichVuTrongDon> ThanhPhanDichVuTrongDonSet => Set<ThanhPhanDichVuTrongDon>();
    public DbSet<PhuPhi> PhuPhiSet => Set<PhuPhi>();
    public DbSet<HoaDon> HoaDonSet => Set<HoaDon>();
    public DbSet<CtHoaDon> CtHoaDonSet => Set<CtHoaDon>();
    public DbSet<TienCoc> TienCocSet => Set<TienCoc>();
    public DbSet<GiaoDichThu> GiaoDichThuSet => Set<GiaoDichThu>();
    public DbSet<HoanCoc> HoanCocSet => Set<HoanCoc>();
    public DbSet<CaLamViec> CaLamViecSet => Set<CaLamViec>();
    public DbSet<KhungGioDatLich> KhungGioDatLichSet => Set<KhungGioDatLich>();
    public DbSet<DangKyCa> DangKyCaSet => Set<DangKyCa>();
    public DbSet<NgayNghi> NgayNghiSet => Set<NgayNghi>();
    public DbSet<DotDangKyCa> DotDangKyCaSet => Set<DotDangKyCa>();
    public DbSet<CauHinhHeThong> CauHinhHeThongSet => Set<CauHinhHeThong>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PetSpaDbContext).Assembly);
    }
}
