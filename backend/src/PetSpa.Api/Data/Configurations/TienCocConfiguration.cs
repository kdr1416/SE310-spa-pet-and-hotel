using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class TienCocConfiguration : IEntityTypeConfiguration<TienCoc>
{
    public void Configure(EntityTypeBuilder<TienCoc> builder)
    {
        builder.ToTable("tien_coc", table =>
        {
            table.HasCheckConstraint("ck_tien_coc_ty_le", "ty_le = 0.30");
            table.HasCheckConstraint("ck_tien_coc_co_so_tinh_snapshot_nonnegative", "co_so_tinh_snapshot >= 0");
            table.HasCheckConstraint("ck_tien_coc_so_tien_yeu_cau_nonnegative", "so_tien_yeu_cau >= 0");
            table.HasCheckConstraint("ck_tien_coc_trang_thai_yeu_cau_values", "trang_thai_yeu_cau IN ('CHO_THU', 'DA_DAP_UNG', 'HET_HAN', 'HUY')");
        });
        builder.HasKey(e => e.Id).HasName("pk_tien_coc");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.CoSoTinhSnapshot).HasColumnName("co_so_tinh_snapshot").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.TyLe).HasColumnName("ty_le").HasColumnType("numeric(4,2)").IsRequired(true);
        builder.Property(e => e.SoTienYeuCau).HasColumnName("so_tien_yeu_cau").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.HetHanAt).HasColumnName("het_han_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.TrangThaiYeuCau).HasColumnName("trang_thai_yeu_cau").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.KetThucAt).HasColumnName("ket_thuc_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.DonDatLichId).HasColumnName("don_dat_lich_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.DonDatLich).WithOne(e => e.TienCocDonDatLich).HasForeignKey<TienCoc>(e => e.DonDatLichId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_tien_coc_don_dat_lich_id");
        builder.HasIndex(e => e.DonDatLichId).HasDatabaseName("ix_tien_coc_don_dat_lich_id").IsUnique();
        builder.HasIndex(e => new { e.TrangThaiYeuCau, e.HetHanAt }).HasDatabaseName("ix_tien_coc_trang_thai_yeu_cau_het_han_at");
    }
}
