using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class BaoVeTrackingConfiguration : IEntityTypeConfiguration<BaoVeTracking>
{
    public void Configure(EntityTypeBuilder<BaoVeTracking> builder)
    {
        builder.ToTable("bao_ve_tracking");
        builder.HasKey(e => e.Id).HasName("pk_bao_ve_tracking");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.KhoaIpHash).HasColumnName("khoa_ip_hash").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.KhoaSdtHash).HasColumnName("khoa_sdt_hash").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.SoLanSai).HasColumnName("so_lan_sai").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.BatDauCuaSo).HasColumnName("bat_dau_cua_so").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.KhoaDen).HasColumnName("khoa_den").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.MoAt).HasColumnName("mo_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.MoBoiId).HasColumnName("mo_boi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.MoBoi).WithMany(e => e.BaoVeTrackingMoBoiItems).HasForeignKey(e => e.MoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_bao_ve_tracking_mo_boi_id");
        builder.HasIndex(e => new { e.KhoaIpHash, e.KhoaSdtHash }).HasDatabaseName("ix_bao_ve_tracking_khoa_ip_hash_khoa_sdt_hash").IsUnique();
    }
}
