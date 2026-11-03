using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class ComboConfiguration : IEntityTypeConfiguration<Combo>
{
    public void Configure(EntityTypeBuilder<Combo> builder)
    {
        builder.ToTable("combo");
        builder.HasKey(e => e.Id).HasName("pk_combo");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenCombo).HasColumnName("ten_combo").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired(true);
        builder.Property(e => e.MoTa).HasColumnName("mo_ta").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
