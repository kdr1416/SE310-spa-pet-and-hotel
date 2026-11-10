using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Services.Billing;

public interface ICatalogService
{
    Task<CatalogResponseDto> GetActiveCatalogAsync();
}

/// <summary>
/// Service dùng dữ liệu Mock từ file MockCatalogData
/// </summary>
public class MockCatalogService : ICatalogService
{
    public Task<CatalogResponseDto> GetActiveCatalogAsync()
    {
        return Task.FromResult(MockCatalogData.GetCatalogResponse());
    }
}

/// <summary>
/// Service kết nối và truy vấn trực tiếp từ CSDL PostgreSQL (PetSpaDbContext)
/// </summary>
public class CatalogService : ICatalogService
{
    private readonly PetSpaDbContext _context;

    public CatalogService(PetSpaDbContext context)
    {
        _context = context;
    }

    public async Task<CatalogResponseDto> GetActiveCatalogAsync()
    {
        var rawSizes = await _context.HeSoCanNangSet
            .AsNoTracking()
            .OrderBy(x => x.CanNangTu)
            .ToListAsync();

        var weightRanges = rawSizes.Select(x => new WeightRangeDto
        {
            MaSize = x.MaSize.ToString(),
            CanNangTu = x.CanNangTu,
            CanNangDen = x.CanNangDen
        }).ToList();

        var rawServices = await _context.DichVuSet
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Include(s => s.BangGiaSizeDichVuItems)
                .ThenInclude(bg => bg.KhoangSize)
            .ToListAsync();

        var services = rawServices.Select(s => new ServiceItemDto
        {
            Id = s.Id,
            TenDichVu = s.TenDichVu,
            LoaiDichVu = s.LoaiDichVu,
            ThoiLuongPhut = s.ThoiLuongPhut,
            YeuCauTiemChung = s.YeuCauTiemChung,
            IsActive = s.IsActive,
            BangGia = s.BangGiaSizeDichVuItems
                .OrderBy(bg => bg.KhoangSize != null ? bg.KhoangSize.CanNangTu : 0)
                .Select(bg => new SizePriceDto
                {
                    MaSize = bg.KhoangSize != null ? bg.KhoangSize.MaSize.ToString() : string.Empty,
                    Gia = bg.DonGia
                })
                .ToList()
        }).ToList();

        return new CatalogResponseDto
        {
            DichVu = services,
            Combo = [],
            LoaiPhong = [],
            KhoangSize = weightRanges
        };
    }
}