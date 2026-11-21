using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;

namespace PetSpa.Api.Services.Billing;

public interface ICatalogService
{
    Task<CatalogDto> GetActiveCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed class CatalogService(PetSpaDbContext context) : ICatalogService
{
    public async Task<CatalogDto> GetActiveCatalogAsync(CancellationToken cancellationToken = default)
    {
        var sizes = await context.HeSoCanNangSet.AsNoTracking().OrderBy(size => size.CanNangTu)
            .Select(size => new CatalogSizeBandDto
            {
                MaSize = size.MaSize.ToString(), TuKg = size.CanNangTu, DenKg = size.CanNangDen
            }).ToListAsync(cancellationToken);

        var services = await context.DichVuSet.AsNoTracking().Where(service => service.IsActive).OrderBy(service => service.Id)
            .Include(service => service.BangGiaSizeDichVuItems).ThenInclude(price => price.KhoangSize)
            .ToListAsync(cancellationToken);

        var combos = await context.ComboSet.AsNoTracking().Where(combo => combo.IsActive).OrderBy(combo => combo.Id)
            .Include(combo => combo.BangGiaSizeComboItems).ThenInclude(price => price.KhoangSize)
            .Include(combo => combo.ChiTietComboComboItems).ThenInclude(item => item.DichVu)
                .ThenInclude(service => service.BangGiaSizeDichVuItems).ThenInclude(price => price.KhoangSize)
            .AsSplitQuery().ToListAsync(cancellationToken);

        var rooms = await context.LoaiPhongSet.AsNoTracking().Where(room => room.IsActive).OrderBy(room => room.Id)
            .Select(room => new CatalogRoomTypeDto
            {
                MaLoaiPhong = room.Id, TenPhong = room.TenPhong, GiaMoiDem = room.GiaMoiDem,
                SoLuongPhong = room.SoLuongPhong, DangHoatDong = room.IsActive
            }).ToListAsync(cancellationToken);

        return new CatalogDto
        {
            DichVu = services.Select(MapService).ToList(),
            Combo = combos.Select(combo => new CatalogComboDto
            {
                MaCombo = combo.Id, TenCombo = combo.TenCombo, MoTa = combo.MoTa, DangHoatDong = combo.IsActive,
                ThanhPhan = combo.ChiTietComboComboItems.OrderBy(item => item.DichVuId).Select(item => MapService(item.DichVu)).ToList(),
                BangGia = MapPrices(combo.BangGiaSizeComboItems)
            }).ToList(),
            LoaiPhong = rooms,
            KhoangSize = sizes
        };
    }

    private static CatalogServiceItemDto MapService(DichVu service) => new()
    {
        MaDichVu = service.Id, TenDichVu = service.TenDichVu, LoaiDichVu = service.LoaiDichVu,
        ThoiLuongPhut = service.ThoiLuongPhut, YeuCauTiemChung = service.YeuCauTiemChung,
        MoTa = service.MoTa, DangHoatDong = service.IsActive, BangGia = MapPrices(service.BangGiaSizeDichVuItems)
    };

    private static List<CatalogSizePriceDto> MapPrices(IEnumerable<BangGiaSize> prices) =>
        prices.OrderBy(price => price.KhoangSize.CanNangTu).Select(price => new CatalogSizePriceDto
        {
            MaSize = price.KhoangSize.MaSize.ToString(), DonGia = price.DonGia
        }).ToList();
}
