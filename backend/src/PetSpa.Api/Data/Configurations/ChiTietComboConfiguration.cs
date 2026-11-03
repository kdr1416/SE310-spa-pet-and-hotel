using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class ChiTietComboConfiguration : IEntityTypeConfiguration<ChiTietCombo>
{
    public void Configure(EntityTypeBuilder<ChiTietCombo> builder)
    {
        builder.ToTable("chi_tiet_combo");
        builder.HasKey(e => new { e.ComboId, e.DichVuId }).HasName("pk_chi_tiet_combo");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ComboId).HasColumnName("combo_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DichVuId).HasColumnName("dich_vu_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.Combo).WithMany(e => e.ChiTietComboComboItems).HasForeignKey(e => e.ComboId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_combo_combo_id");
        builder.HasOne(e => e.DichVu).WithMany(e => e.ChiTietComboDichVuItems).HasForeignKey(e => e.DichVuId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_chi_tiet_combo_dich_vu_id");
    }
}
