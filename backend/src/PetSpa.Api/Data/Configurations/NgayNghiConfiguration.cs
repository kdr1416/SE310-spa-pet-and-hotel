using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class NgayNghiConfiguration : IEntityTypeConfiguration<NgayNghi>
{
    public void Configure(EntityTypeBuilder<NgayNghi> builder)
    {
        builder.ToTable("ngay_nghi");
        builder.HasKey(e => e.Id).HasName("pk_ngay_nghi");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.Ngay).HasColumnName("ngay").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.LyDo).HasColumnName("ly_do").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.NgayNghiTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ngay_nghi_tao_boi_id");
        builder.HasIndex(e => e.Ngay).HasDatabaseName("ix_ngay_nghi_ngay").IsUnique();
    }
}
