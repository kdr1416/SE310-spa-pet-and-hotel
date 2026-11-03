using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DanhGiaConfiguration : IEntityTypeConfiguration<DanhGia>
{
    public void Configure(EntityTypeBuilder<DanhGia> builder)
    {
        builder.ToTable("danh_gia", table =>
        {
            table.HasCheckConstraint("ck_danh_gia_so_sao", "so_sao BETWEEN 1 AND 5");
        });
        builder.HasKey(e => e.Id).HasName("pk_danh_gia");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.SoSao).HasColumnName("so_sao").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.BinhLuan).HasColumnName("binh_luan").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.AnhPaths).HasColumnName("anh_paths").HasColumnType("jsonb").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DonDatLichId).HasColumnName("don_dat_lich_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.KhachHangId).HasColumnName("khach_hang_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ChiTietDonId).HasColumnName("chi_tiet_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.DichVuTrongDonId).HasColumnName("dich_vu_trong_don_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.DonDatLich).WithOne(e => e.DanhGiaDonDatLich).HasForeignKey<DanhGia>(e => e.DonDatLichId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_danh_gia_don_dat_lich_id");
        builder.HasOne(e => e.KhachHang).WithMany(e => e.DanhGiaKhachHangItems).HasForeignKey(e => e.KhachHangId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_danh_gia_khach_hang_id");
        builder.HasOne(e => e.ChiTietDon).WithMany(e => e.DanhGiaChiTietDonItems).HasForeignKey(e => e.ChiTietDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_danh_gia_chi_tiet_don_id");
        builder.HasOne(e => e.DichVuTrongDon).WithMany(e => e.DanhGiaDichVuTrongDonItems).HasForeignKey(e => e.DichVuTrongDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_danh_gia_dich_vu_trong_don_id");
    }
}
