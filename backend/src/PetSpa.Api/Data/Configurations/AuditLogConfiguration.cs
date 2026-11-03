using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Api.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(e => e.Id).HasName("pk_audit_log");
        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("integer").IsRequired(true).UseIdentityByDefaultColumn();
        builder.Property(e => e.ActorLoai).HasColumnName("actor_loai").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.HanhDong).HasColumnName("hanh_dong").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.LoaiDoiTuong).HasColumnName("loai_doi_tuong").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasColumnType("character varying(40)").HasMaxLength(40).IsRequired(true);
        builder.Property(e => e.DoiTuongId).HasColumnName("doi_tuong_id").HasColumnType("integer").IsRequired(false);
        builder.Property(e => e.LyDo).HasColumnName("ly_do").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.Truoc).HasColumnName("truoc").HasColumnType("jsonb").IsRequired(false);
        builder.Property(e => e.Sau).HasColumnName("sau").HasColumnType("jsonb").IsRequired(false);
        builder.Property(e => e.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasColumnType("text").IsRequired(false);
        builder.Property(e => e.KetQua).HasColumnName("ket_qua").HasColumnType("jsonb").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired(true).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ActorId).HasColumnName("actor_id").HasColumnType("integer").IsRequired(false);
        builder.HasOne(e => e.Actor).WithMany(e => e.AuditLogActorItems).HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_audit_log_actor_id");
        builder.HasIndex(e => new { e.ActorId, e.HanhDong, e.RequestId }).HasDatabaseName("ix_audit_log_actor_id_hanh_dong_request_id").IsUnique().HasFilter("request_id IS NOT NULL");
        builder.HasIndex(e => new { e.HanhDong, e.RequestId }).HasDatabaseName("ix_audit_log_anonymous_request").IsUnique().HasFilter("actor_id IS NULL AND request_id IS NOT NULL");
    }
}
