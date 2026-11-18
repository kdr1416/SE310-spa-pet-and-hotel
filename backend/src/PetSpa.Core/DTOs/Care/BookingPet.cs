namespace PetSpa.Core.DTOs.Care;

public class BookingPet
{
    public int MaChiTietDon { get; set; }
    public int MaThuCung { get; set; }
    public string TenThuCung { get; set; } = string.Empty;
    public string TrangThai { get; set; } = string.Empty;
    public decimal? CanNangChot { get; set; }
    public string? SizeThucTe { get; set; }
    public string? SizeChot { get; set; }
    public List<string>? TinhTrangLong { get; set; }
    public int? MaLoaiPhong { get; set; }
    public string? TenNhanVienPhuTrach { get; set; }
    public List<OrderService> DichVu { get; set; } = new();
    public List<CareLog> NhatKy { get; set; } = new();
    public bool NhanSom { get; set; }
    public DateTimeOffset? BanGiaoAt { get; set; }
    public DateTimeOffset? TraLaiAt { get; set; }
}

public class OrderService
{
    public int MaDichVuTrongDon { get; set; }
    public int? MaDichVu { get; set; }
    public int? MaCombo { get; set; }
    public int MaKhungGio { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string TrangThai { get; set; } = string.Empty;
    public string? SizeChot { get; set; }
    public decimal? GiaChot { get; set; }
    public List<Component> ThanhPhan { get; set; } = new();
}

public class Component
{
    public int MaThanhPhan { get; set; }
    public int MaDichVu { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string TrangThai { get; set; } = string.Empty;
    public DateTimeOffset? HoanTatAt { get; set; }
}

public class CareLog
{
    public int MaNhatKy { get; set; }
    public string LoaiNhatKy { get; set; } = string.Empty;
    public string? GhiChu { get; set; }
    public string? AnhUrl { get; set; }
    public decimal? CanNangTheoDoi { get; set; }
    public string TenTacGia { get; set; } = string.Empty;
    public DateTimeOffset GhiNhanAt { get; set; }
}
