using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DonDatLichConfiguration : IEntityTypeConfiguration<DonDatLich>
{
    public void Configure(EntityTypeBuilder<DonDatLich> builder)
    {
        builder.ToTable("don_dat_lich", table =>
        {
            table.HasCheckConstraint("ck_don_dat_lich_ngay_du_kien", "ket_thuc_du_kien >= bat_dau_du_kien");
            table.HasCheckConstraint("ck_don_dat_lich_trang_thai_values", "trang_thai IN ('PENDING', 'CONFIRMED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')");
            table.HasCheckConstraint("ck_don_dat_lich_tong_tam_tinh_nonnegative", "tong_tam_tinh >= 0");
            table.HasCheckConstraint("ck_don_dat_lich_loai_huy_values", "loai_huy IN ('CUSTOMER_CANCELLED', 'STAFF_REJECTED', 'NO_SHOW', 'STAFF_CANCELLED')");
        });
        builder.HasKey(e => e.Id).HasName("pk_don_dat_lich");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.TrackingCode).HasColumnName("tracking_code").HasColumnType("character varying(8)").HasMaxLength(8).IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.TenNguoiDat).HasColumnName("ten_nguoi_dat").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.SoDienThoaiSnapshot).HasColumnName("so_dien_thoai_snapshot").HasColumnType("character varying(15)").HasMaxLength(15).IsRequired(true);
        builder.Property(e => e.EmailSnapshot).HasColumnName("email_snapshot").HasColumnType("character varying(254)").HasMaxLength(254).IsRequired(true);
        builder.Property(e => e.BatDauDuKien).HasColumnName("bat_dau_du_kien").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.KetThucDuKien).HasColumnName("ket_thuc_du_kien").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.MocCuoiCa).HasColumnName("moc_cuoi_ca").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.TongTamTinh).HasColumnName("tong_tam_tinh").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.ChinhSachSnapshot).HasColumnName("chinh_sach_snapshot").HasColumnType("jsonb").IsRequired(true);
        builder.Property(e => e.EmailDelivery).HasColumnName("email_delivery").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(false);
        builder.Property(e => e.EmailAttemptAt).HasColumnName("email_attempt_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.DuyetAt).HasColumnName("duyet_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.HuyAt).HasColumnName("huy_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.HoanTatAt).HasColumnName("hoan_tat_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.LoaiHuy).HasColumnName("loai_huy").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.LyDoHuy).HasColumnName("ly_do_huy").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.KhachHangId).HasColumnName("khach_hang_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.KhungGioId).HasColumnName("khung_gio_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.DuyetBoiId).HasColumnName("duyet_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.HuyBoiId).HasColumnName("huy_boi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.KhachHang).WithMany(e => e.DonDatLichKhachHangItems).HasForeignKey(e => e.KhachHangId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_don_dat_lich_khach_hang_id");
        builder.HasOne(e => e.KhungGio).WithMany(e => e.DonDatLichKhungGioItems).HasForeignKey(e => e.KhungGioId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_don_dat_lich_khung_gio_id");
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.DonDatLichTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_don_dat_lich_tao_boi_id");
        builder.HasOne(e => e.DuyetBoi).WithMany(e => e.DonDatLichDuyetBoiItems).HasForeignKey(e => e.DuyetBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_don_dat_lich_duyet_boi_id");
        builder.HasOne(e => e.HuyBoi).WithMany(e => e.DonDatLichHuyBoiItems).HasForeignKey(e => e.HuyBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_don_dat_lich_huy_boi_id");
        builder.HasIndex(e => e.RequestId).HasDatabaseName("ix_don_dat_lich_request_id").IsUnique();
        builder.HasIndex(e => e.TrackingCode).HasDatabaseName("ix_don_dat_lich_tracking_code").IsUnique();
        builder.HasIndex(e => new { e.TrangThai, e.KhungGioId }).HasDatabaseName("ix_don_dat_lich_trang_thai_khung_gio_id");
    }
}
