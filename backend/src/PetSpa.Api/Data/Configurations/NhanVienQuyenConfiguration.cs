using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class NhanVienQuyenConfiguration : IEntityTypeConfiguration<NhanVienQuyen>
{
    public void Configure(EntityTypeBuilder<NhanVienQuyen> builder)
    {
        builder.ToTable("nhan_vien_quyen");
        builder.HasKey(e => new { e.NhanVienId, e.MaQuyen }).HasName("pk_nhan_vien_quyen");
        builder.Property(e => e.CapAt).HasColumnName("cap_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.NhanVienId).HasColumnName("nhan_vien_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.MaQuyen).HasColumnName("ma_quyen").HasColumnType("character varying(60)").HasMaxLength(60).IsRequired(true);
        builder.Property(e => e.CapBoiId).HasColumnName("cap_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.NhanVien).WithMany(e => e.NhanVienQuyenNhanVienItems).HasForeignKey(e => e.NhanVienId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_nhan_vien_quyen_nhan_vien_id");
        builder.HasOne(e => e.Quyen).WithMany(e => e.NhanVienQuyenQuyenItems).HasForeignKey(e => e.MaQuyen).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_nhan_vien_quyen_ma_quyen");
        builder.HasOne(e => e.CapBoi).WithMany(e => e.NhanVienQuyenCapBoiItems).HasForeignKey(e => e.CapBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_nhan_vien_quyen_cap_boi_id");
    }
}
