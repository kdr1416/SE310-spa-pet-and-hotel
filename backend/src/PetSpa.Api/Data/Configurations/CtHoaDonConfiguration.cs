using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class CtHoaDonConfiguration : IEntityTypeConfiguration<CtHoaDon>
{
    public void Configure(EntityTypeBuilder<CtHoaDon> builder)
    {
        builder.ToTable("ct_hoa_don", table =>
        {
            table.HasCheckConstraint("ck_ct_hoa_don_so_luong", "so_luong > 0");
            table.HasCheckConstraint("ck_ct_hoa_don_thanh_tien", "thanh_tien = so_luong * don_gia_snapshot");
            table.HasCheckConstraint("ck_ct_hoa_don_nguon", "(loai_khoan = 'SPA' AND chi_tiet_don_id IS NOT NULL AND dich_vu_trong_don_id IS NOT NULL AND phu_phi_id IS NULL) OR (loai_khoan = 'HOTEL' AND chi_tiet_don_id IS NOT NULL AND dich_vu_trong_don_id IS NULL AND phu_phi_id IS NULL) OR (loai_khoan = 'PHU_PHI' AND phu_phi_id IS NOT NULL AND dich_vu_trong_don_id IS NULL)");
            table.HasCheckConstraint("ck_ct_hoa_don_size_snapshot_values", "size_snapshot IN ('S', 'M', 'L', 'XL')");
            table.HasCheckConstraint("ck_ct_hoa_don_don_gia_snapshot_nonnegative", "don_gia_snapshot >= 0");
            table.HasCheckConstraint("ck_ct_hoa_don_thanh_tien_nonnegative", "thanh_tien >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_ct_hoa_don");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.LoaiKhoan).HasColumnName("loai_khoan").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.TenPetSnapshot).HasColumnName("ten_pet_snapshot").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(false);
        builder.Property(e => e.TenKhoanMuc).HasColumnName("ten_khoan_muc").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.SizeSnapshot).HasColumnName("size_snapshot").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.SoLuong).HasColumnName("so_luong").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.DonGiaSnapshot).HasColumnName("don_gia_snapshot").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.ThanhTien).HasColumnName("thanh_tien").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.DienGiai).HasColumnName("dien_giai").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.HoaDonId).HasColumnName("hoa_don_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ChiTietDonId).HasColumnName("chi_tiet_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.DichVuTrongDonId).HasColumnName("dich_vu_trong_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.PhuPhiId).HasColumnName("phu_phi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.HoaDon).WithMany(e => e.CtHoaDonHoaDonItems).HasForeignKey(e => e.HoaDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ct_hoa_don_hoa_don_id");
        builder.HasOne(e => e.ChiTietDon).WithMany(e => e.CtHoaDonChiTietDonItems).HasForeignKey(e => e.ChiTietDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ct_hoa_don_chi_tiet_don_id");
        builder.HasOne(e => e.DichVuTrongDon).WithMany(e => e.CtHoaDonDichVuTrongDonItems).HasForeignKey(e => e.DichVuTrongDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ct_hoa_don_dich_vu_trong_don_id");
        builder.HasOne(e => e.PhuPhi).WithMany(e => e.CtHoaDonPhuPhiItems).HasForeignKey(e => e.PhuPhiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ct_hoa_don_phu_phi_id");
    }
}
