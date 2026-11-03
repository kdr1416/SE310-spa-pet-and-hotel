using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class ThuCungConfiguration : IEntityTypeConfiguration<ThuCung>
{
    public void Configure(EntityTypeBuilder<ThuCung> builder)
    {
        builder.ToTable("thu_cung", table =>
        {
            table.HasCheckConstraint("ck_thu_cung_can_nang", "can_nang > 0");
            table.HasCheckConstraint("ck_thu_cung_loai_pet_values", "loai_pet IN ('CHO', 'MEO')");
        });
        builder.HasKey(e => e.Id).HasName("pk_thu_cung");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.TenThuCung).HasColumnName("ten_thu_cung").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(true);
        builder.Property(e => e.LoaiPet).HasColumnName("loai_pet").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.CanNang).HasColumnName("can_nang").HasColumnType("numeric(5,2)").IsRequired(true);
        builder.Property(e => e.Giong).HasColumnName("giong").HasColumnType("character varying(150)").HasMaxLength(150).IsRequired(false);
        builder.Property(e => e.NgaySinh).HasColumnName("ngay_sinh").HasColumnType("date").IsRequired(false);
        builder.Property(e => e.LoaiLong).HasColumnName("loai_long").HasColumnType("character varying(100)").HasMaxLength(100).IsRequired(false);
        builder.Property(e => e.GhiChuDiUng).HasColumnName("ghi_chu_di_ung").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.KhachHangId).HasColumnName("khach_hang_id").HasColumnType("integer").IsRequired(true);
        builder.HasOne(e => e.KhachHang).WithMany(e => e.ThuCungKhachHangItems).HasForeignKey(e => e.KhachHangId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_thu_cung_khach_hang_id");
    }
}
