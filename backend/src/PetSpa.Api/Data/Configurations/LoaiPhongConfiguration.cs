using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class LoaiPhongConfiguration : IEntityTypeConfiguration<LoaiPhong>
{
    public void Configure(EntityTypeBuilder<LoaiPhong> builder)
    {
        builder.ToTable("loai_phong", table =>
        {
            table.HasCheckConstraint("ck_loai_phong_suc_chua", "so_luong_phong >= 0");
            table.HasCheckConstraint("ck_loai_phong_gia_moi_dem_nonnegative", "gia_moi_dem >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_loai_phong");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenPhong).HasColumnName("ten_phong").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.GiaMoiDem).HasColumnName("gia_moi_dem").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.SoLuongPhong).HasColumnName("so_luong_phong").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
