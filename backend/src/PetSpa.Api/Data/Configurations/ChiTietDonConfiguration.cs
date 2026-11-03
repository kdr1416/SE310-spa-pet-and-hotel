using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class ChiTietDonConfiguration : IEntityTypeConfiguration<ChiTietDon>
{
    public void Configure(EntityTypeBuilder<ChiTietDon> builder)
    {
        builder.ToTable("chi_tiet_don", table =>
        {
            table.HasCheckConstraint("ck_chi_tiet_don_trang_thai_cham_soc_values", "trang_thai_cham_soc IN ('NOT_ARRIVED', 'WAITING', 'IN_PROGRESS', 'PAUSED', 'READY', 'DONE', 'CANCELLED')");
            table.HasCheckConstraint("ck_chi_tiet_don_can_nang_chot_positive", "can_nang_chot > 0");
            table.HasCheckConstraint("ck_chi_tiet_don_size_thuc_te_values", "size_thuc_te IN ('S', 'M', 'L', 'XL')");
            table.HasCheckConstraint("ck_chi_tiet_don_size_chot_values", "size_chot IN ('S', 'M', 'L', 'XL')");
            table.HasCheckConstraint("ck_chi_tiet_don_gia_phong_cam_ket_nonnegative", "gia_phong_cam_ket >= 0");
            table.HasCheckConstraint("ck_chi_tiet_don_gia_phong_chot_nonnegative", "gia_phong_chot >= 0");
            table.HasCheckConstraint("ck_chi_tiet_don_tinh_trang_long_values", "tinh_trang_long IN ('BINH_THUONG', 'ROI_NHE', 'ROI_NANG', 'VE_RAN')");
            table.HasCheckConstraint("ck_chi_tiet_don_can_nang_khai_bao_positive", "can_nang_khai_bao > 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_chi_tiet_don");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenPetSnapshot).HasColumnName("ten_pet_snapshot").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.TrangThaiChamSoc).HasColumnName("trang_thai_cham_soc").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true).HasDefaultValue(PetCareState.NOT_ARRIVED);
        builder.Property(e => e.CanNangChot).HasColumnName("can_nang_chot").HasColumnType("numeric(5,2)").IsRequired(false);
        builder.Property(e => e.SizeThucTe).HasColumnName("size_thuc_te").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.SizeChot).HasColumnName("size_chot").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.ThoaThuanGia).HasColumnName("thoa_thuan_gia").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.GiaPhongCamKet).HasColumnName("gia_phong_cam_ket").HasColumnType("numeric(14,0)").IsRequired(false);
        builder.Property(e => e.GiaPhongChot).HasColumnName("gia_phong_chot").HasColumnType("numeric(14,0)").IsRequired(false);
        builder.Property(e => e.SoDemChot).HasColumnName("so_dem_chot").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.CheckInAt).HasColumnName("check_in_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.BanGiaoAt).HasColumnName("ban_giao_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.TraLaiAt).HasColumnName("tra_lai_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.NhanSom).HasColumnName("nhan_som").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.TinhTrangLong).HasColumnName("tinh_trang_long").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.GhiChuSucKhoe).HasColumnName("ghi_chu_suc_khoe").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.XacMinhTiemAt).HasColumnName("xac_minh_tiem_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.HuyAt).HasColumnName("huy_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.LyDoHuy).HasColumnName("ly_do_huy").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.SoDemDaO).HasColumnName("so_dem_da_o").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.NhanSomLyDo).HasColumnName("nhan_som_ly_do").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DonDatLichId).HasColumnName("don_dat_lich_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ThuCungId).HasColumnName("thu_cung_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.LoaiPhongId).HasColumnName("loai_phong_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.NhanVienId).HasColumnName("nhan_vien_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.HoSoTiemChungId).HasColumnName("ho_so_tiem_chung_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.XacMinhTiemBoiId).HasColumnName("xac_minh_tiem_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.CheckInBoiId).HasColumnName("check_in_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.BanGiaoBoiId).HasColumnName("ban_giao_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.HuyBoiId).HasColumnName("huy_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.NhanSomBoiId).HasColumnName("nhan_som_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.CanNangKhaiBao).HasColumnName("can_nang_khai_bao").HasColumnType("numeric(5,2)").IsRequired(true);
        builder.Property(e => e.ReadyAt).HasColumnName("ready_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasOne(e => e.DonDatLich).WithMany(e => e.ChiTietDonDonDatLichItems).HasForeignKey(e => e.DonDatLichId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_don_dat_lich_id");
        builder.HasOne(e => e.ThuCung).WithMany(e => e.ChiTietDonThuCungItems).HasForeignKey(e => e.ThuCungId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_thu_cung_id");
        builder.HasOne(e => e.LoaiPhong).WithMany(e => e.ChiTietDonLoaiPhongItems).HasForeignKey(e => e.LoaiPhongId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_loai_phong_id");
        builder.HasOne(e => e.NhanVien).WithMany(e => e.ChiTietDonNhanVienItems).HasForeignKey(e => e.NhanVienId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_nhan_vien_id");
        builder.HasOne(e => e.HoSoTiemChung).WithMany(e => e.ChiTietDonHoSoTiemChungItems).HasForeignKey(e => e.HoSoTiemChungId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_ho_so_tiem_chung_id");
        builder.HasOne(e => e.XacMinhTiemBoi).WithMany(e => e.ChiTietDonXacMinhTiemBoiItems).HasForeignKey(e => e.XacMinhTiemBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_xac_minh_tiem_boi_id");
        builder.HasOne(e => e.CheckInBoi).WithMany(e => e.ChiTietDonCheckInBoiItems).HasForeignKey(e => e.CheckInBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_check_in_boi_id");
        builder.HasOne(e => e.BanGiaoBoi).WithMany(e => e.ChiTietDonBanGiaoBoiItems).HasForeignKey(e => e.BanGiaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_ban_giao_boi_id");
        builder.HasOne(e => e.HuyBoi).WithMany(e => e.ChiTietDonHuyBoiItems).HasForeignKey(e => e.HuyBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_huy_boi_id");
        builder.HasOne(e => e.NhanSomBoi).WithMany(e => e.ChiTietDonNhanSomBoiItems).HasForeignKey(e => e.NhanSomBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_don_nhan_som_boi_id");
        builder.HasIndex(e => new { e.DonDatLichId, e.ThuCungId }).HasDatabaseName("ix_chi_tiet_don_don_dat_lich_id_thu_cung_id").IsUnique();
        builder.HasIndex(e => new { e.NhanVienId, e.TrangThaiChamSoc }).HasDatabaseName("ix_chi_tiet_don_nhan_vien_id_trang_thai_cham_soc");
    }
}
