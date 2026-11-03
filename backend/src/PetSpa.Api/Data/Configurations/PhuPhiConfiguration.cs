using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class PhuPhiConfiguration : IEntityTypeConfiguration<PhuPhi>
{
    public void Configure(EntityTypeBuilder<PhuPhi> builder)
    {
        builder.ToTable("phu_phi", table =>
        {
            table.HasCheckConstraint("ck_phu_phi_so_tien", "so_tien > 0");
            table.HasCheckConstraint("ck_phu_phi_so_tien_nonnegative", "so_tien >= 0");
        });
        builder.HasKey(e => e.Id).HasName("pk_phu_phi");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenPhuPhi).HasColumnName("ten_phu_phi").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.SoTien).HasColumnName("so_tien").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.LyDo).HasColumnName("ly_do").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.HuyAt).HasColumnName("huy_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DonDatLichId).HasColumnName("don_dat_lich_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.ChiTietDonId).HasColumnName("chi_tiet_don_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.DonDatLich).WithMany(e => e.PhuPhiDonDatLichItems).HasForeignKey(e => e.DonDatLichId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_phu_phi_don_dat_lich_id");
        builder.HasOne(e => e.ChiTietDon).WithMany(e => e.PhuPhiChiTietDonItems).HasForeignKey(e => e.ChiTietDonId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_phu_phi_chi_tiet_don_id");
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.PhuPhiTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_phu_phi_tao_boi_id");
    }
}
