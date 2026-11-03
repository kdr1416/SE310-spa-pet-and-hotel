using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Billing;

public class DichVuTrongDon
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    public bool LaPhatSinh { get; set; }
    public string TenSnapshot { get; set; } = string.Empty;
    public string BangGiaSizeSnapshot { get; set; } = "{}";
    public int ThoiLuongSnapshot { get; set; }
    public OrderServiceState TrangThai { get; set; }
    public SizeBand? SizeChot { get; set; }
    public decimal? GiaChot { get; set; }
    public string? LyDo { get; set; }
    public DateTimeOffset? HoanTatAt { get; set; }
    public DateTimeOffset? HuyAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int ChiTietDonId { get; set; }
    public int? DichVuId { get; set; }
    public int? ComboId { get; set; }
    public int KhungGioId { get; set; }

    public ChiTietDon ChiTietDon { get; set; } = null!;
    public DichVu? DichVu { get; set; }
    public Combo? Combo { get; set; }
    public KhungGioDatLich KhungGio { get; set; } = null!;
    public ICollection<DanhGia> DanhGiaDichVuTrongDonItems { get; set; } = new List<DanhGia>();
    public ICollection<ThanhPhanDichVuTrongDon> ThanhPhanDichVuTrongDonDichVuTrongDonItems { get; set; } = new List<ThanhPhanDichVuTrongDon>();
    public ICollection<CtHoaDon> CtHoaDonDichVuTrongDonItems { get; set; } = new List<CtHoaDon>();
}
