using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class HoanCoc
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public decimal SoTien { get; set; }
    public string LyDo { get; set; } = string.Empty;
    public RefundState TrangThai { get; set; }
    public DateTimeOffset? HoanAt { get; set; }
    public PaymentMethod? PhuongThuc { get; set; }
    public string? ThamChieu { get; set; }
    public string? BangChungPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int TienCocId { get; set; }
    public int TaoBoiId { get; set; }
    public int? HoanBoiId { get; set; }

    public TienCoc TienCoc { get; set; } = null!;
    public NhanVien TaoBoi { get; set; } = null!;
    public NhanVien? HoanBoi { get; set; }
}
