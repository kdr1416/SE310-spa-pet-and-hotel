using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class QuyenConfiguration : IEntityTypeConfiguration<Quyen>
{
    public void Configure(EntityTypeBuilder<Quyen> builder)
    {
        builder.ToTable("quyen");
        builder.HasKey(e => e.MaQuyen).HasName("pk_quyen");
        builder.Property(e => e.MaQuyen).HasColumnName("ma_quyen").HasColumnType("character varying(60)").HasMaxLength(60).IsRequired(true);
        builder.Property(e => e.TenQuyen).HasColumnName("ten_quyen").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
