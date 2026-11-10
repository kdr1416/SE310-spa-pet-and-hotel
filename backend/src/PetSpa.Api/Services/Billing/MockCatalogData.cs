using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Services.Billing;

public static class MockCatalogData
{
    public static List<WeightRangeDto> GetWeightRanges() =>
    [
        new() { MaSize = "S", CanNangTu = 0, CanNangDen = 5 },
        new() { MaSize = "M", CanNangTu = 5, CanNangDen = 15 },
        new() { MaSize = "L", CanNangTu = 15, CanNangDen = 25 },
        new() { MaSize = "XL", CanNangTu = 25, CanNangDen = null }
    ];

    public static List<ServiceItemDto> GetServices() =>
    [
        new()
        {
            Id = 1,
            TenDichVu = "Tắm sấy vệ sinh",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            YeuCauTiemChung = false,
            IsActive = true,
            BangGia =
            [
                new() { MaSize = "S", Gia = 150000 },
                new() { MaSize = "M", Gia = 200000 },
                new() { MaSize = "L", Gia = 250000 },
                new() { MaSize = "XL", Gia = 300000 }
            ]
        },
        new()
        {
            Id = 2,
            TenDichVu = "Cắt tỉa tạo kiểu",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 90,
            YeuCauTiemChung = false,
            IsActive = true,
            BangGia =
            [
                new() { MaSize = "S", Gia = 250000 },
                new() { MaSize = "M", Gia = 350000 },
                new() { MaSize = "L", Gia = 450000 },
                new() { MaSize = "XL", Gia = 600000 }
            ]
        },
        new()
        {
            Id = 3,
            TenDichVu = "Vệ sinh tai",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 30,
            YeuCauTiemChung = false,
            IsActive = true,
            BangGia =
            [
                new() { MaSize = "S", Gia = 50000 },
                new() { MaSize = "M", Gia = 70000 },
                new() { MaSize = "L", Gia = 90000 },
                new() { MaSize = "XL", Gia = 110000 }
            ]
        }
    ];

    public static CatalogResponseDto GetCatalogResponse() => new()
    {
        DichVu = GetServices(),
        Combo = [],
        LoaiPhong = [],
        KhoangSize = GetWeightRanges()
    };
}