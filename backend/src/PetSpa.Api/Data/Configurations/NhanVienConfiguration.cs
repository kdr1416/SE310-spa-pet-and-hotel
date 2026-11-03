using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class NhanVienConfiguration : IEntityTypeConfiguration<NhanVien>
{
    public void Configure(EntityTypeBuilder<NhanVien> builder)
    {
        builder.ToTable("nhan_vien", table =>
        {
            table.HasCheckConstraint("ck_nhan_vien_vai_tro_values", "vai_tro IN ('ADMIN', 'STAFF')");
        });
        builder.HasKey(e => e.Id).HasName("pk_nhan_vien");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenNhanVien).HasColumnName("ten_nhan_vien").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.TenDangNhap).HasColumnName("ten_dang_nhap").HasColumnType("character varying(50)").HasMaxLength(50).IsRequired(true);
        builder.Property(e => e.MatKhauHash).HasColumnName("mat_khau_hash").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.VaiTro).HasColumnName("vai_tro").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.Email).HasColumnName("email").HasColumnType("character varying(254)").HasMaxLength(254).IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(e => e.TenDangNhap).HasDatabaseName("ix_nhan_vien_ten_dang_nhap").IsUnique();
    }
}
