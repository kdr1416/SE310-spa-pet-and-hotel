using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Identity;

public class OtpChallenge
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public OtpScope MucDich { get; set; }
    public string Email { get; set; } = string.Empty;
    public string SoDienThoai { get; set; } = string.Empty;
    public string OtpDigest { get; set; } = string.Empty;
    public DateTimeOffset HetHanAt { get; set; }
    public DateTimeOffset GuiLaiSau { get; set; }
    public int SoLanSai { get; set; }
    public DateTimeOffset? SuDungAt { get; set; }
    public string? ProofDigest { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? KhachHangId { get; set; }

    public KhachHang? KhachHang { get; set; }
}
