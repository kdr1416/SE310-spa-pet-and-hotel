using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Services.Billing;

public interface ICatalogService
{
    Task<CatalogDto> GetActiveCatalogAsync(CancellationToken cancellationToken = default);
}

public class CatalogService(PetSpaDbContext context) : ICatalogService
{
    public async Task<CatalogDto> GetActiveCatalogAsync(CancellationToken cancellationToken = default)
    {
        var rawSizes = await context.HeSoCanNangSet
            .AsNoTracking()
            .OrderBy(x => x.CanNangTu)
            .ToListAsync(cancellationToken);

        var weightRanges = rawSizes.Select(x => new CatalogSizeBandDto
        {
            MaSize = x.MaSize.ToString(),
            TuKg = x.CanNangTu,
            DenKg = x.CanNangDen
        }).ToList();

        var rawServices = await context.DichVuSet
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .Include(s => s.BangGiaSizeDichVuItems)
                .ThenInclude(bg => bg.KhoangSize)
            .ToListAsync(cancellationToken);

        var services = rawServices.Select(s => new CatalogServiceItemDto
        {
            MaDichVu = s.Id,
            TenDichVu = s.TenDichVu,
            LoaiDichVu = s.LoaiDichVu,
            ThoiLuongPhut = s.ThoiLuongPhut,
            YeuCauTiemChung = s.YeuCauTiemChung,
            MoTa = s.MoTa,
            DangHoatDong = s.IsActive,
            BangGia = s.BangGiaSizeDichVuItems
                .OrderBy(bg => bg.KhoangSize != null ? bg.KhoangSize.CanNangTu : 0)
                .Select(bg => new CatalogSizePriceDto
                {
                    MaSize = bg.KhoangSize != null ? bg.KhoangSize.MaSize.ToString() : string.Empty,
                    DonGia = bg.DonGia
                })
                .ToList()
        }).ToList();

        return new CatalogDto
        {
            DichVu = services,
            Combo = [],
            LoaiPhong = [],
            KhoangSize = weightRanges
        };
    }
}
