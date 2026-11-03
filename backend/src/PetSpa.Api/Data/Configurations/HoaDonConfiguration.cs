using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class HoaDonConfiguration : IEntityTypeConfiguration<HoaDon>
{
    public void Configure(EntityTypeBuilder<HoaDon> builder)
    {
        builder.ToTable("hoa_don", table =>
        {
            table.HasCheckConstraint("ck_hoa_don_tong_tien", "tong_tien >= coc_khau_tru AND con_phai_tra = tong_tien - coc_khau_tru");
            table.HasCheckConstraint("ck_hoa_don_khong_tu_thay_the", "thay_the_hoa_don_id IS NULL OR thay_the_hoa_don_id <> id");
            table.HasCheckConstraint("ck_hoa_don_trang_thai_values", "trang_thai IN ('UNPAID', 'PAID', 'REVOKED')");
            table.HasCheckConstraint("ck_hoa_don_tong_tien_nonnegative", "tong_tien >= 0");
            table.HasCheckConstraint("ck_hoa_don_coc_khau_tru_nonnegative", "coc_khau_tru >= 0");
            table.HasCheckConstraint("ck_hoa_don_con_phai_tra_nonnegative", "con_phai_tra >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_hoa_don");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.MaHoaDon).HasColumnName("ma_hoa_don").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.TongTien).HasColumnName("tong_tien").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.CocKhauTru).HasColumnName("coc_khau_tru").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.ConPhaiTra).HasColumnName("con_phai_tra").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.KhachSnapshot).HasColumnName("khach_snapshot").HasColumnType("jsonb").IsRequired(true);
        builder.Property(e => e.PaidAt).HasColumnName("paid_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.ThuHoiAt).HasColumnName("thu_hoi_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.LyDoThuHoi).HasColumnName("ly_do_thu_hoi").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DonDatLichId).HasColumnName("don_dat_lich_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ThayTheHoaDonId).HasColumnName("thay_the_hoa_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ThuBoiId).HasColumnName("thu_boi_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.ThuHoiBoiId).HasColumnName("thu_hoi_boi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.DonDatLich).WithMany(e => e.HoaDonDonDatLichItems).HasForeignKey(e => e.DonDatLichId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoa_don_don_dat_lich_id");
        builder.HasOne(e => e.ThayTheHoaDon).WithOne(e => e.HoaDonThayTheHoaDon).HasForeignKey<HoaDon>(e => e.ThayTheHoaDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoa_don_thay_the_hoa_don_id");
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.HoaDonTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoa_don_tao_boi_id");
        builder.HasOne(e => e.ThuBoi).WithMany(e => e.HoaDonThuBoiItems).HasForeignKey(e => e.ThuBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoa_don_thu_boi_id");
        builder.HasOne(e => e.ThuHoiBoi).WithMany(e => e.HoaDonThuHoiBoiItems).HasForeignKey(e => e.ThuHoiBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoa_don_thu_hoi_boi_id");
        builder.HasIndex(e => e.MaHoaDon).HasDatabaseName("ix_hoa_don_ma_hoa_don").IsUnique();
        builder.HasIndex(e => e.ThayTheHoaDonId).HasDatabaseName("ix_hoa_don_thay_the_hoa_don_id").IsUnique().HasFilter("thay_the_hoa_don_id IS NOT NULL");
        builder.HasIndex(e => e.DonDatLichId).HasDatabaseName("ix_hoa_don_don_dat_lich_id").IsUnique().HasFilter("trang_thai <> 'REVOKED'");
        builder.HasIndex(e => e.PaidAt).HasDatabaseName("ix_hoa_don_paid_at");
    }
}
