using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class CauHinhHeThongConfiguration : IEntityTypeConfiguration<CauHinhHeThong>
{
    public void Configure(EntityTypeBuilder<CauHinhHeThong> builder)
    {
        builder.ToTable("cau_hinh_he_thong");
        builder.HasKey(e => e.Id).HasName("pk_cau_hinh_he_thong");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.Khoa).HasColumnName("khoa").HasColumnType("character varying(100)").HasMaxLength(100).IsRequired(true);
        builder.Property(e => e.GiaTri).HasColumnName("gia_tri").HasColumnType("jsonb").IsRequired(true);
        builder.Property(e => e.PhienBan).HasColumnName("phien_ban").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.SuaBoiId).HasColumnName("sua_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.SuaBoi).WithMany(e => e.CauHinhHeThongSuaBoiItems).HasForeignKey(e => e.SuaBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_cau_hinh_he_thong_sua_boi_id");
        builder.HasIndex(e => e.Khoa).HasDatabaseName("ix_cau_hinh_he_thong_khoa").IsUnique();
    }
}
