using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class HoSoTiemChungConfiguration : IEntityTypeConfiguration<HoSoTiemChung>
{
    public void Configure(EntityTypeBuilder<HoSoTiemChung> builder)
    {
        builder.ToTable("ho_so_tiem_chung", table =>
        {
            table.HasCheckConstraint("ck_ho_so_tiem_chung_hieu_luc", "hieu_luc_den IS NULL OR hieu_luc_den >= ngay_tiem");
        });
        builder.HasKey(e => e.Id).HasName("pk_ho_so_tiem_chung");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.LoaiVaccine).HasColumnName("loai_vaccine").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.NgayTiem).HasColumnName("ngay_tiem").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.HieuLucDen).HasColumnName("hieu_luc_den").HasColumnType("date").IsRequired(false);
        builder.Property(e => e.ObjectPath).HasColumnName("object_path").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CoSoCap).HasColumnName("co_so_cap").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(false);
        builder.Property(e => e.GhiChu).HasColumnName("ghi_chu").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ThuCungId).HasColumnName("thu_cung_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.ThuCung).WithMany(e => e.HoSoTiemChungThuCungItems).HasForeignKey(e => e.ThuCungId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ho_so_tiem_chung_thu_cung_id");
    }
}
