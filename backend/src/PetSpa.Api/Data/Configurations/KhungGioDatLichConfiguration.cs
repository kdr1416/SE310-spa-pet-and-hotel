using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class KhungGioDatLichConfiguration : IEntityTypeConfiguration<KhungGioDatLich>
{
    public void Configure(EntityTypeBuilder<KhungGioDatLich> builder)
    {
        builder.ToTable("khung_gio_dat_lich", table =>
        {
            table.HasCheckConstraint("ck_khung_gio_dat_lich_slot", "tong_slot >= 0 AND slot_da_dat >= 0 AND slot_da_dat <= tong_slot");
            table.HasCheckConstraint("ck_khung_gio_dat_lich_phut", "tran_phut >= 0 AND phut_da_giu >= 0 AND phut_da_giu <= tran_phut");
        });
        builder.HasKey(e => e.Id).HasName("pk_khung_gio_dat_lich");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.Ngay).HasColumnName("ngay").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.MoNhanKhach).HasColumnName("mo_nhan_khach").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.TongSlot).HasColumnName("tong_slot").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.SlotDaDat).HasColumnName("slot_da_dat").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TranPhut).HasColumnName("tran_phut").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.PhutDaGiu).HasColumnName("phut_da_giu").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.CaLamViecId).HasColumnName("ca_lam_viec_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.CaLamViec).WithMany(e => e.KhungGioDatLichCaLamViecItems).HasForeignKey(e => e.CaLamViecId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_khung_gio_dat_lich_ca_lam_viec_id");
        builder.HasIndex(e => new { e.Ngay, e.CaLamViecId }).HasDatabaseName("ix_khung_gio_dat_lich_ngay_ca_lam_viec_id").IsUnique();
    }
}
