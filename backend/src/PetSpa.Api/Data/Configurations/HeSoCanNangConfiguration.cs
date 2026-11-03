using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class HeSoCanNangConfiguration : IEntityTypeConfiguration<HeSoCanNang>
{
    public void Configure(EntityTypeBuilder<HeSoCanNang> builder)
    {
        builder.ToTable("he_so_can_nang", table =>
        {
            table.HasCheckConstraint("ck_he_so_can_nang_khoang", "can_nang_tu >= 0 AND (can_nang_den IS NULL OR can_nang_den > can_nang_tu)");
            table.HasCheckConstraint("ck_he_so_can_nang_ma_size_values", "ma_size IN ('S', 'M', 'L', 'XL')");
        });
        builder.HasKey(e => e.Id).HasName("pk_he_so_can_nang");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.MaSize).HasColumnName("ma_size").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.CanNangTu).HasColumnName("can_nang_tu").HasColumnType("numeric(5,2)").IsRequired(true);
        builder.Property(e => e.CanNangDen).HasColumnName("can_nang_den").HasColumnType("numeric(5,2)").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(e => e.MaSize).HasDatabaseName("ix_he_so_can_nang_ma_size").IsUnique();
    }
}
