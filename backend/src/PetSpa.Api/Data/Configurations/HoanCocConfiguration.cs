using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class HoanCocConfiguration : IEntityTypeConfiguration<HoanCoc>
{
    public void Configure(EntityTypeBuilder<HoanCoc> builder)
    {
        builder.ToTable("hoan_coc", table =>
        {
            table.HasCheckConstraint("ck_hoan_coc_so_tien", "so_tien > 0");
            table.HasCheckConstraint("ck_hoan_coc_so_tien_nonnegative", "so_tien >= 0");
            table.HasCheckConstraint("ck_hoan_coc_trang_thai_values", "trang_thai IN ('CHO_HOAN', 'DA_HOAN')");
            table.HasCheckConstraint("ck_hoan_coc_phuong_thuc_values", "phuong_thuc IN ('CASH', 'BANK_TRANSFER')");
        });
        builder.HasKey(e => e.Id).HasName("pk_hoan_coc");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(e => e.SoTien).HasColumnName("so_tien").HasColumnType("numeric(14,0)").IsRequired(true);
        builder.Property(e => e.LyDo).HasColumnName("ly_do").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.TrangThai).HasColumnName("trang_thai").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.HoanAt).HasColumnName("hoan_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.PhuongThuc).HasColumnName("phuong_thuc").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(false);
        builder.Property(e => e.ThamChieu).HasColumnName("tham_chieu").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(false);
        builder.Property(e => e.BangChungPath).HasColumnName("bang_chung_path").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.TienCocId).HasColumnName("tien_coc_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.HoanBoiId).HasColumnName("hoan_boi_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.TienCoc).WithMany(e => e.HoanCocTienCocItems).HasForeignKey(e => e.TienCocId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoan_coc_tien_coc_id");
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.HoanCocTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoan_coc_tao_boi_id");
        builder.HasOne(e => e.HoanBoi).WithMany(e => e.HoanCocHoanBoiItems).HasForeignKey(e => e.HoanBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_hoan_coc_hoan_boi_id");
        builder.HasIndex(e => e.RequestId).HasDatabaseName("ix_hoan_coc_request_id").IsUnique();
    }
}
