using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DangKyCaConfiguration : IEntityTypeConfiguration<DangKyCa>
{
    public void Configure(EntityTypeBuilder<DangKyCa> builder)
    {
        builder.ToTable("dang_ky_ca", table =>
        {
            table.HasCheckConstraint("ck_dang_ky_ca_trang_thai_values", "trang_thai IN ('PENDING', 'APPROVED', 'REJECTED')");
        });
        builder.HasKey(e => e.Id).HasName("pk_dang_ky_ca");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.Ngay).HasColumnName("ngay").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.DuyetAt).HasColumnName("duyet_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.LyDoDieuChuyen).HasColumnName("ly_do_dieu_chuyen").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.NhanVienId).HasColumnName("nhan_vien_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.CaLamViecId).HasColumnName("ca_lam_viec_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DotId).HasColumnName("dot_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DuyetBoiId).HasColumnName("duyet_boi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.NhanVien).WithMany(e => e.DangKyCaNhanVienItems).HasForeignKey(e => e.NhanVienId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dang_ky_ca_nhan_vien_id");
        builder.HasOne(e => e.CaLamViec).WithMany(e => e.DangKyCaCaLamViecItems).HasForeignKey(e => e.CaLamViecId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dang_ky_ca_ca_lam_viec_id");
        builder.HasOne(e => e.Dot).WithMany(e => e.DangKyCaDotItems).HasForeignKey(e => e.DotId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dang_ky_ca_dot_id");
        builder.HasOne(e => e.DuyetBoi).WithMany(e => e.DangKyCaDuyetBoiItems).HasForeignKey(e => e.DuyetBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dang_ky_ca_duyet_boi_id");
        builder.HasIndex(e => new { e.NhanVienId, e.Ngay, e.CaLamViecId }).HasDatabaseName("ix_dang_ky_ca_nhan_vien_id_ngay_ca_lam_viec_id").IsUnique();
        builder.HasIndex(e => new { e.Ngay, e.TrangThai }).HasDatabaseName("ix_dang_ky_ca_ngay_trang_thai");
    }
}
