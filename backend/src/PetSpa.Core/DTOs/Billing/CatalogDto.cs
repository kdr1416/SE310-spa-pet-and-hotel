namespace PetSpa.Core.DTOs.Billing;

public class CatalogSizePriceDto
{
    public string MaSize { get; set; } = string.Empty; // S, M, L, XL
    public decimal DonGia { get; set; }
}

public class CatalogServiceItemDto
{
    public int MaDichVu { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string LoaiDichVu { get; set; } = string.Empty; // SPA
    public int ThoiLuongPhut { get; set; }
    public bool YeuCauTiemChung { get; set; }
    public List<CatalogSizePriceDto> BangGia { get; set; } = [];
    public string? MoTa { get; set; }
    public bool DangHoatDong { get; set; }
}

public class CatalogComboDto
{
    public int MaCombo { get; set; }
    public string TenCombo { get; set; } = string.Empty;
    public List<CatalogServiceItemDto> ThanhPhan { get; set; } = [];
    public List<CatalogSizePriceDto> BangGia { get; set; } = [];
    public string? MoTa { get; set; }
    public bool DangHoatDong { get; set; }
}

public class CatalogRoomTypeDto
{
    public int MaLoaiPhong { get; set; }
    public string TenPhong { get; set; } = string.Empty;
    public decimal GiaMoiDem { get; set; }
    public int SoLuongPhong { get; set; }
    public bool DangHoatDong { get; set; }
}

public class CatalogSizeBandDto
{
    public string MaSize { get; set; } = string.Empty;
    public decimal TuKg { get; set; }
    public decimal? DenKg { get; set; } // Null đối với size XL
}

public class CatalogDto
{
    public List<CatalogServiceItemDto> DichVu { get; set; } = [];
    public List<CatalogComboDto> Combo { get; set; } = [];
    public List<CatalogRoomTypeDto> LoaiPhong { get; set; } = [];
    public List<CatalogSizeBandDto> KhoangSize { get; set; } = [];
}
