using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class GiaoDichThuConfiguration : IEntityTypeConfiguration<GiaoDichThu>
{
    public void Configure(EntityTypeBuilder<GiaoDichThu> builder)
    {
        builder.ToTable("giao_dich_thu", table =>
        {
            table.HasCheckConstraint("ck_giao_dich_thu_dich_thu", "(tien_coc_id IS NOT NULL) <> (hoa_don_id IS NOT NULL)");
            table.HasCheckConstraint("ck_giao_dich_thu_so_tien", "so_tien > 0");
            table.HasCheckConstraint("ck_giao_dich_thu_so_tien_nonnegative", "so_tien >= 0");
            table.HasCheckConstraint("ck_giao_dich_thu_phuong_thuc_values", "phuong_thuc IN ('CASH', 'BANK_TRANSFER')");
        });
        builder.HasKey(e => e.Id).HasName("pk_giao_dich_thu");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(e => e.SoTien).HasColumnName("so_tien").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.PhuongThuc).HasColumnName("phuong_thuc").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.NganHang).HasColumnName("ngan_hang").HasColumnType("character varying(100)").HasMaxLength(100).IsRequired(false);
        builder.Property(e => e.MaGiaoDich).HasColumnName("ma_giao_dich").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(false);
        builder.Property(e => e.GiaoDichAt).HasColumnName("giao_dich_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.XacNhanAt).HasColumnName("xac_nhan_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.GhiChu).HasColumnName("ghi_chu").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.TienCocId).HasColumnName("tien_coc_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.HoaDonId).HasColumnName("hoa_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.XacNhanBoiId).HasColumnName("xac_nhan_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.TienCoc).WithMany(e => e.GiaoDichThuTienCocItems).HasForeignKey(e => e.TienCocId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_giao_dich_thu_tien_coc_id");
        builder.HasOne(e => e.HoaDon).WithMany(e => e.GiaoDichThuHoaDonItems).HasForeignKey(e => e.HoaDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_giao_dich_thu_hoa_don_id");
        builder.HasOne(e => e.XacNhanBoi).WithMany(e => e.GiaoDichThuXacNhanBoiItems).HasForeignKey(e => e.XacNhanBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_giao_dich_thu_xac_nhan_boi_id");
        builder.HasIndex(e => e.RequestId).HasDatabaseName("ix_giao_dich_thu_request_id").IsUnique();
        builder.HasIndex(e => new { e.NganHang, e.MaGiaoDich }).HasDatabaseName("ix_giao_dich_thu_ngan_hang_ma_giao_dich").IsUnique().HasFilter("ngan_hang IS NOT NULL AND ma_giao_dich IS NOT NULL");
    }
}
