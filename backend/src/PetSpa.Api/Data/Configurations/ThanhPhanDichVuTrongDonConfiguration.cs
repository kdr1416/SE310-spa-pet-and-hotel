using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class ThanhPhanDichVuTrongDonConfiguration : IEntityTypeConfiguration<ThanhPhanDichVuTrongDon>
{
    public void Configure(EntityTypeBuilder<ThanhPhanDichVuTrongDon> builder)
    {
        builder.ToTable("thanh_phan_dich_vu_trong_don", table =>
        {
            table.HasCheckConstraint("ck_thanh_phan_dich_vu_trong_don_trang_thai_values", "trang_thai IN ('CHO_THUC_HIEN', 'HOAN_TAT', 'HUY')");
        });
        builder.HasKey(e => e.Id).HasName("pk_thanh_phan_dich_vu_trong_don");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenSnapshot).HasColumnName("ten_snapshot").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.GiaLeSizeSnapshot).HasColumnName("gia_le_size_snapshot").HasColumnType("jsonb").IsRequired(true);
        builder.Property(e => e.ThoiLuongSnapshot).HasColumnName("thoi_luong_snapshot").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.HoanTatAt).HasColumnName("hoan_tat_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DichVuTrongDonId).HasColumnName("dich_vu_trong_don_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DichVuId).HasColumnName("dich_vu_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.DichVuTrongDon).WithMany(e => e.ThanhPhanDichVuTrongDonDichVuTrongDonItems).HasForeignKey(e => e.DichVuTrongDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_thanh_phan_dich_vu_trong_don_dich_vu_trong_don_id");
        builder.HasOne(e => e.DichVu).WithMany(e => e.ThanhPhanDichVuTrongDonDichVuItems).HasForeignKey(e => e.DichVuId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_thanh_phan_dich_vu_trong_don_dich_vu_id");
        builder.HasIndex(e => new { e.DichVuTrongDonId, e.DichVuId }).HasDatabaseName("ix_thanh_phan_dich_vu_trong_don_dich_vu_trong_don_id_dich_vu_id").IsUnique();
    }
}
