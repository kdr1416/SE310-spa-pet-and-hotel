using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class BangGiaSizeConfiguration : IEntityTypeConfiguration<BangGiaSize>
{
    public void Configure(EntityTypeBuilder<BangGiaSize> builder)
    {
        builder.ToTable("bang_gia_size", table =>
        {
            table.HasCheckConstraint("ck_bang_gia_size_nguon", "(dich_vu_id IS NOT NULL) <> (combo_id IS NOT NULL)");
            table.HasCheckConstraint("ck_bang_gia_size_don_gia_nonnegative", "don_gia >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_bang_gia_size");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.DonGia).HasColumnName("don_gia").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DichVuId).HasColumnName("dich_vu_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.ComboId).HasColumnName("combo_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.SizeId).HasColumnName("size_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.DichVu).WithMany(e => e.BangGiaSizeDichVuItems).HasForeignKey(e => e.DichVuId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_bang_gia_size_dich_vu_id");
        builder.HasOne(e => e.Combo).WithMany(e => e.BangGiaSizeComboItems).HasForeignKey(e => e.ComboId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_bang_gia_size_combo_id");
        builder.HasOne(e => e.KhoangSize).WithMany(e => e.BangGiaSizeKhoangSizeItems).HasForeignKey(e => e.SizeId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_bang_gia_size_size_id");
        builder.HasIndex(e => new { e.DichVuId, e.SizeId }).HasDatabaseName("ix_bang_gia_size_dich_vu_id_size_id").IsUnique().HasFilter("dich_vu_id IS NOT NULL");
        builder.HasIndex(e => new { e.ComboId, e.SizeId }).HasDatabaseName("ix_bang_gia_size_combo_id_size_id").IsUnique().HasFilter("combo_id IS NOT NULL");
    }
}
