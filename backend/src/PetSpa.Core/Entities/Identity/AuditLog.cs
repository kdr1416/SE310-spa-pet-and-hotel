using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class AuditLog
{
    public int Id { get; set; }
    public string ActorLoai { get; set; } = string.Empty;
    public string HanhDong { get; set; } = string.Empty;
    public string LoaiDoiTuong { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public int? DoiTuongId { get; set; }
    public string? LyDo { get; set; }
    public string? Truoc { get; set; }
    public string? Sau { get; set; }
    public Guid? RequestId { get; set; }
    public string? Fingerprint { get; set; }
    public string? KetQua { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ActorId { get; set; }

    public NhanVien? Actor { get; set; }
}
