using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DichVuConfiguration : IEntityTypeConfiguration<DichVu>
{
    public void Configure(EntityTypeBuilder<DichVu> builder)
    {
        builder.ToTable("dich_vu", table =>
        {
            table.HasCheckConstraint("ck_dich_vu_thoi_luong", "thoi_luong_phut > 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_dich_vu");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenDichVu).HasColumnName("ten_dich_vu").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.LoaiDichVu).HasColumnName("loai_dich_vu").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.ThoiLuongPhut).HasColumnName("thoi_luong_phut").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.YeuCauTiemChung).HasColumnName("yeu_cau_tiem_chung").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.MoTa).HasColumnName("mo_ta").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
