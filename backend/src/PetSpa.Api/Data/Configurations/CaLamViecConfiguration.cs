using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class CaLamViecConfiguration : IEntityTypeConfiguration<CaLamViec>
{
    public void Configure(EntityTypeBuilder<CaLamViec> builder)
    {
        builder.ToTable("ca_lam_viec", table =>
        {
            table.HasCheckConstraint("ck_ca_lam_viec_gio", "gio_ket_thuc > gio_bat_dau");
            table.HasCheckConstraint("ck_ca_lam_viec_suc_chua", "suc_chua_pet >= 0 AND tran_phut >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_ca_lam_viec");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenCa).HasColumnName("ten_ca").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.GioBatDau).HasColumnName("gio_bat_dau").HasColumnType("time without time zone").IsRequired(true);
        builder.Property(e => e.GioKetThuc).HasColumnName("gio_ket_thuc").HasColumnType("time without time zone").IsRequired(true);
        builder.Property(e => e.SucChuaPet).HasColumnName("suc_chua_pet").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TranPhut).HasColumnName("tran_phut").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
