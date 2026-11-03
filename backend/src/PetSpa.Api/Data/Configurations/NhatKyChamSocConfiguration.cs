using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class NhatKyChamSocConfiguration : IEntityTypeConfiguration<NhatKyChamSoc>
{
    public void Configure(EntityTypeBuilder<NhatKyChamSoc> builder)
    {
        builder.ToTable("nhat_ky_cham_soc", table =>
        {
            table.HasCheckConstraint("ck_nhat_ky_cham_soc_noi_dung", "object_path IS NOT NULL OR ghi_chu IS NOT NULL");
            table.HasCheckConstraint("ck_nhat_ky_cham_soc_can_nang_ghi_nhan_positive", "can_nang_ghi_nhan > 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_nhat_ky_cham_soc");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.LoaiNhatKy).HasColumnName("loai_nhat_ky").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.GhiNhanAt).HasColumnName("ghi_nhan_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.ObjectPath).HasColumnName("object_path").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.GhiChu).HasColumnName("ghi_chu").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CanNangGhiNhan).HasColumnName("can_nang_ghi_nhan").HasColumnType("numeric(5,2)").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ChiTietDonId).HasColumnName("chi_tiet_don_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.NhanVienId).HasColumnName("nhan_vien_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.ChiTietDon).WithMany(e => e.NhatKyChamSocChiTietDonItems).HasForeignKey(e => e.ChiTietDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_nhat_ky_cham_soc_chi_tiet_don_id");
        builder.HasOne(e => e.NhanVien).WithMany(e => e.NhatKyChamSocNhanVienItems).HasForeignKey(e => e.NhanVienId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_nhat_ky_cham_soc_nhan_vien_id");
        builder.HasIndex(e => new { e.ChiTietDonId, e.GhiNhanAt }).HasDatabaseName("ix_nhat_ky_cham_soc_chi_tiet_don_id_ghi_nhan_at");
    }
}
