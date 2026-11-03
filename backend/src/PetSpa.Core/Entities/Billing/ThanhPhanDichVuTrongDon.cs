using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class ThanhPhanDichVuTrongDon
{
    public int Id { get; set; }
    public string TenSnapshot { get; set; } = string.Empty;
    public string GiaLeSizeSnapshot { get; set; } = "{}";
    public int ThoiLuongSnapshot { get; set; }
    public OrderServiceState TrangThai { get; set; }
    public DateTimeOffset? HoanTatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int DichVuTrongDonId { get; set; }
    public int DichVuId { get; set; }

    public DichVuTrongDon DichVuTrongDon { get; set; } = null!;
    public DichVu DichVu { get; set; } = null!;
}
