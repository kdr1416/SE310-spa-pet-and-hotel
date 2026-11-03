using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenge", table =>
        {
            table.HasCheckConstraint("ck_otp_challenge_muc_dich_values", "muc_dich IN ('BOOKING', 'EXISTING_PETS')");
        });
        builder.HasKey(e => e.Id).HasName("pk_otp_challenge");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(e => e.MucDich).HasColumnName("muc_dich").HasConversion<string>().HasColumnType("character varying(40)").IsRequired(true);
        builder.Property(e => e.Email).HasColumnName("email").HasColumnType("character varying(254)").HasMaxLength(254).IsRequired(true);
        builder.Property(e => e.SoDienThoai).HasColumnName("so_dien_thoai").HasColumnType("character varying(15)").HasMaxLength(15).IsRequired(true);
        builder.Property(e => e.OtpDigest).HasColumnName("otp_digest").HasColumnType("text").IsRequired(true);
        builder.Property(e => e.HetHanAt).HasColumnName("het_han_at").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.GuiLaiSau).HasColumnName("gui_lai_sau").HasColumnType("timestamp with time zone").IsRequired(true);
        builder.Property(e => e.SoLanSai).HasColumnName("so_lan_sai").HasColumnType("integer").IsRequired(true);
        builder.Property(e => e.SuDungAt).HasColumnName("su_dung_at").HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(e => e.ProofDigest).HasColumnName("proof_digest").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.KhachHangId).HasColumnName("khach_hang_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.KhachHang).WithMany(e => e.OtpChallengeKhachHangItems).HasForeignKey(e => e.KhachHangId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_otp_challenge_khach_hang_id");
        builder.HasIndex(e => e.RequestId).HasDatabaseName("ix_otp_challenge_request_id").IsUnique();
    }
}
