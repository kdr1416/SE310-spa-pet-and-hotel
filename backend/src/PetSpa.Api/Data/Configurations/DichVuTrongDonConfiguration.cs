using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DichVuTrongDonConfiguration : IEntityTypeConfiguration<DichVuTrongDon>
{
    public void Configure(EntityTypeBuilder<DichVuTrongDon> builder)
    {
        builder.ToTable("dich_vu_trong_don", table =>
        {
            table.HasCheckConstraint("ck_dich_vu_trong_don_nguon", "(dich_vu_id IS NOT NULL) <> (combo_id IS NOT NULL)");
            table.HasCheckConstraint("ck_dich_vu_trong_don_thoi_luong", "thoi_luong_snapshot > 0");
            table.HasCheckConstraint("ck_dich_vu_trong_don_trang_thai_values", "trang_thai IN ('CHO_THUC_HIEN', 'HOAN_TAT', 'HUY')");
            table.HasCheckConstraint("ck_dich_vu_trong_don_size_chot_values", "size_chot IN ('S', 'M', 'L', 'XL')");
            table.HasCheckConstraint("ck_dich_vu_trong_don_gia_chot_nonnegative", "gia_chot >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_dich_vu_trong_don");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(e => e.LaPhatSinh).HasColumnName("la_phat_sinh").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.TenSnapshot).HasColumnName("ten_snapshot").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.BangGiaSizeSnapshot).HasColumnName("bang_gia_size_snapshot").HasColumnType("jsonb").IsRequired(true);
        builder.Property(e => e.ThoiLuongSnapshot).HasColumnName("thoi_luong_snapshot").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.SizeChot).HasColumnName("size_chot").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.GiaChot).HasColumnName("gia_chot").HasColumnType("numeric(14,0)").IsRequired(false);
        builder.Property(e => e.LyDo).HasColumnName("ly_do").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.HoanTatAt).HasColumnName("hoan_tat_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.HuyAt).HasColumnName("huy_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ChiTietDonId).HasColumnName("chi_tiet_don_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DichVuId).HasColumnName("dich_vu_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.ComboId).HasColumnName("combo_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.KhungGioId).HasColumnName("khung_gio_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.ChiTietDon).WithMany(e => e.DichVuTrongDonChiTietDonItems).HasForeignKey(e => e.ChiTietDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dich_vu_trong_don_chi_tiet_don_id");
        builder.HasOne(e => e.DichVu).WithMany(e => e.DichVuTrongDonDichVuItems).HasForeignKey(e => e.DichVuId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dich_vu_trong_don_dich_vu_id");
        builder.HasOne(e => e.Combo).WithMany(e => e.DichVuTrongDonComboItems).HasForeignKey(e => e.ComboId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dich_vu_trong_don_combo_id");
        builder.HasOne(e => e.KhungGio).WithMany(e => e.DichVuTrongDonKhungGioItems).HasForeignKey(e => e.KhungGioId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dich_vu_trong_don_khung_gio_id");
        builder.HasIndex(e => e.RequestId).HasDatabaseName("ix_dich_vu_trong_don_request_id").IsUnique();
        builder.HasIndex(e => new { e.KhungGioId, e.TrangThai }).HasDatabaseName("ix_dich_vu_trong_don_khung_gio_id_trang_thai");
    }
}
