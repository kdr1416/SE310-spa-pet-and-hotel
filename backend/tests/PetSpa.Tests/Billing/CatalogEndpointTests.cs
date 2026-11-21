using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Billing;

public class CatalogEndpointTests
{
    [Fact]
    public async Task GetCatalog_WhenActiveServicesExist_ReturnsOkWithActiveServicesAndFourSizes()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedSizesAndServices(db, includeActiveServices: true));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CatalogDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();

        body.Data!.KhoangSize.Should().HaveCount(4);
        body.Data.KhoangSize.Select(x => x.MaSize).Should().ContainInOrder("S", "M", "L", "XL");
        body.Data.KhoangSize.Last().DenKg.Should().BeNull();

        body.Data.DichVu.Should().ContainSingle();
        var service = body.Data.DichVu[0];
        service.TenDichVu.Should().Be("Tắm sấy vệ sinh");
        service.DangHoatDong.Should().BeTrue();
        service.MoTa.Should().Be("Tắm sấy cơ bản");
        service.BangGia.Should().HaveCount(4);
        service.BangGia.Select(x => x.MaSize).Should().ContainInOrder("S", "M", "L", "XL");
        service.BangGia[0].DonGia.Should().Be(150_000m);
    }

    [Fact]
    public async Task GetCatalog_WhenNoActiveServicesExist_ReturnsOkWithEmptyDichVuArray()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedSizesAndServices(db, includeActiveServices: false));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CatalogDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.DichVu.Should().BeEmpty();
        body.Data.Combo.Should().BeEmpty();
        body.Data.LoaiPhong.Should().BeEmpty();
        body.Data.KhoangSize.Should().HaveCount(4);
    }

    private static void SeedSizesAndServices(PetSpaDbContext db, bool includeActiveServices)
    {
        var now = DateTimeOffset.UtcNow;
        var sizeS = new HeSoCanNang { MaSize = SizeBand.S, CanNangTu = 0m, CanNangDen = 5m, CreatedAt = now, UpdatedAt = now };
        var sizeM = new HeSoCanNang { MaSize = SizeBand.M, CanNangTu = 5m, CanNangDen = 15m, CreatedAt = now, UpdatedAt = now };
        var sizeL = new HeSoCanNang { MaSize = SizeBand.L, CanNangTu = 15m, CanNangDen = 25m, CreatedAt = now, UpdatedAt = now };
        var sizeXL = new HeSoCanNang { MaSize = SizeBand.XL, CanNangTu = 25m, CanNangDen = null, CreatedAt = now, UpdatedAt = now };
        db.HeSoCanNangSet.AddRange(sizeS, sizeM, sizeL, sizeXL);
        db.SaveChanges();

        db.DichVuSet.Add(new DichVu
        {
            TenDichVu = "Dịch vụ tạm ngưng",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 45,
            YeuCauTiemChung = false,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (includeActiveServices)
        {
            var activeService = new DichVu
            {
                TenDichVu = "Tắm sấy vệ sinh",
                LoaiDichVu = "SPA",
                ThoiLuongPhut = 60,
                YeuCauTiemChung = true,
                IsActive = true,
                MoTa = "Tắm sấy cơ bản",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.DichVuSet.Add(activeService);
            db.SaveChanges();

            db.BangGiaSizeSet.AddRange(
                new BangGiaSize { DichVuId = activeService.Id, SizeId = sizeS.Id, DonGia = 150_000m, CreatedAt = now, UpdatedAt = now },
                new BangGiaSize { DichVuId = activeService.Id, SizeId = sizeM.Id, DonGia = 200_000m, CreatedAt = now, UpdatedAt = now },
                new BangGiaSize { DichVuId = activeService.Id, SizeId = sizeL.Id, DonGia = 250_000m, CreatedAt = now, UpdatedAt = now },
                new BangGiaSize { DichVuId = activeService.Id, SizeId = sizeXL.Id, DonGia = 300_000m, CreatedAt = now, UpdatedAt = now }
            );
        }

        db.SaveChanges();
    }
}
