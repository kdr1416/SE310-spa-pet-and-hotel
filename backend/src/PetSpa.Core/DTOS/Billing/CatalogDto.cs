namespace PetSpa.Core.DTOs.Billing;

public class SizePriceDto
{
    public string MaSize { get; set; } = string.Empty; // S, M, L, XL
    public decimal Gia { get; set; }
}

public class ServiceItemDto
{
    public int Id { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string LoaiDichVu { get; set; } = string.Empty; // SPA
    public int ThoiLuongPhut { get; set; }
    public bool YeuCauTiemChung { get; set; }
    public bool IsActive { get; set; }
    public List<SizePriceDto> BangGia { get; set; } = new();
}

public class WeightRangeDto
{
    public string MaSize { get; set; } = string.Empty;
    public decimal CanNangTu { get; set; }
    public decimal? CanNangDen { get; set; } // Null đối với size XL
}

public class CatalogResponseDto
{
    public List<ServiceItemDto> DichVu { get; set; } = new();
    public List<object> Combo { get; set; } = new(); // Sprint 1 chưa nhận combo
    public List<object> LoaiPhong { get; set; } = new(); // Sprint 1 chưa nhận phòng
    public List<WeightRangeDto> KhoangSize { get; set; } = new();
}