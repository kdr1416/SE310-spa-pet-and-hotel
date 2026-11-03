using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class DotDangKyCaConfiguration : IEntityTypeConfiguration<DotDangKyCa>
{
    public void Configure(EntityTypeBuilder<DotDangKyCa> builder)
    {
        builder.ToTable("dot_dang_ky_ca", table =>
        {
            table.HasCheckConstraint("ck_dot_dang_ky_ca_khoang", "tu_ngay <= den_ngay AND mo_at < dong_at");
        });
        builder.HasKey(e => e.Id).HasName("pk_dot_dang_ky_ca");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TuNgay).HasColumnName("tu_ngay").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.DenNgay).HasColumnName("den_ngay").HasColumnType("date").IsRequired(true);
        builder.Property(e => e.MoAt).HasColumnName("mo_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.DongAt).HasColumnName("dong_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.TaoBoiId).HasColumnName("tao_boi_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.TaoBoi).WithMany(e => e.DotDangKyCaTaoBoiItems).HasForeignKey(e => e.TaoBoiId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_dot_dang_ky_ca_tao_boi_id");
    }
}
