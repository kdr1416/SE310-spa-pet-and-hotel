using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Data.Seeding;

public static class DbInitializer
{
    private static readonly string[] Permissions =
    [
        "CATALOG_VIEW", "BOOKING_CREATE", "TRACKING_VIEW", "CUSTOMER_LOOKUP", "BOOKING_APPROVE",
        "BOOKING_RESCHEDULE", "CHECKIN_PERFORM", "CARE_UPDATE", "PHOTO_UPLOAD", "PET_HANDOVER",
        "INVOICE_CREATE", "INVOICE_COLLECT", "INVOICE_REVOKE", "PET_CANCEL_LINE", "SCHEDULE_REGISTER",
        "SCHEDULE_APPROVE", "SYSTEM_CONFIG", "EARLY_HANDOVER_APPROVE", "DEPOSIT_CONFIRM", "DEPOSIT_REFUND",
        "TRACKING_UNLOCK", "PERMISSION_MANAGE", "PHOTO_EXCEPTION_APPROVE", "REVIEW_READ"
    ];

    public static async Task InitializeAsync(PetSpaDbContext context, SeedDataOptions options,
        IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !environment.IsStaging())
            throw new InvalidOperationException("Seed data is allowed only in Development, Testing or Staging.");
        options.Validate();
        var date = options.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Serialize cooperating seed runs without changing the schema or business identifiers.
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2026110302)", cancellationToken);
        var admin = await EnsureAccountAsync(context, "admin", "Quản trị viên", Role.ADMIN, options.AdminPassword, cancellationToken);
        var reception = await EnsureAccountAsync(context, "staff1", "Nhân viên lễ tân", Role.STAFF, options.Staff1Password, cancellationToken);
        var care = await EnsureAccountAsync(context, "staff2", "Kỹ thuật viên Spa", Role.STAFF, options.Staff2Password, cancellationToken);
        await SeedPermissionsAsync(context, admin, reception, care, cancellationToken);
        await SeedCatalogAsync(context, cancellationToken);
        await SeedScheduleAsync(context, admin, care, date, cancellationToken);
        if (!await context.CauHinhHeThongSet.AnyAsync(s => s.Khoa == "EXTENSION_SETTINGS", cancellationToken))
        {
            context.CauHinhHeThongSet.Add(new CauHinhHeThong
            {
                Khoa = "EXTENSION_SETTINGS", PhienBan = 1, SuaBoiId = admin.Id,
                GiaTri = "{\"otpEmail\":false,\"cocHotel\":false,\"giuCho\":false}"
            });
        }
        await CatalogSeed.EnsureFurConditionsAsync(context, admin.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<NhanVien> EnsureAccountAsync(PetSpaDbContext context, string username,
        string name, Role role, string password, CancellationToken cancellationToken)
    {
        var account = await context.NhanVienSet.SingleOrDefaultAsync(e => e.TenDangNhap == username, cancellationToken);
        if (account is not null)
        {
            if (account.VaiTro != role || !account.IsActive)
                throw new InvalidOperationException($"Seed account '{username}' conflicts with an existing role or inactive account; no account was reset.");
            return account;
        }
        account = new NhanVien { TenDangNhap = username, TenNhanVien = name, VaiTro = role, IsActive = true };
        account.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(account, password);
        context.NhanVienSet.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static async Task SeedPermissionsAsync(PetSpaDbContext context, NhanVien admin,
        NhanVien reception, NhanVien care, CancellationToken cancellationToken)
    {
        var existing = (await context.QuyenSet.Select(p => p.MaQuyen).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var permission in Permissions.Where(p => !existing.Contains(p)))
            context.QuyenSet.Add(new Quyen { MaQuyen = permission, TenQuyen = permission });
        await context.SaveChangesAsync(cancellationToken);

        string[] receptionPermissions = ["CATALOG_VIEW", "BOOKING_CREATE", "TRACKING_VIEW", "CUSTOMER_LOOKUP",
            "BOOKING_APPROVE", "CHECKIN_PERFORM", "PET_HANDOVER", "INVOICE_CREATE", "INVOICE_COLLECT",
            "SCHEDULE_REGISTER", "DEPOSIT_CONFIRM"];
        string[] carePermissions = ["CATALOG_VIEW", "CARE_UPDATE", "PHOTO_UPLOAD", "SCHEDULE_REGISTER"];
        var grants = (await context.NhanVienQuyenSet.ToListAsync(cancellationToken))
            .Select(p => (p.NhanVienId, p.MaQuyen)).ToHashSet();
        foreach (var (employee, permissions) in new[] { (admin, Permissions), (reception, receptionPermissions), (care, carePermissions) })
        {
            foreach (var permission in permissions.Where(p => !grants.Contains((employee.Id, p))))
                context.NhanVienQuyenSet.Add(new NhanVienQuyen
                {
                    NhanVienId = employee.Id, MaQuyen = permission, CapBoiId = admin.Id, CapAt = DateTimeOffset.UtcNow
                });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCatalogAsync(PetSpaDbContext context, CancellationToken cancellationToken)
    {
        var sizes = await context.HeSoCanNangSet.ToListAsync(cancellationToken);
        (SizeBand Code, decimal From, decimal? To)[] bands =
            [(SizeBand.S, 0, 5), (SizeBand.M, 5, 15), (SizeBand.L, 15, 25), (SizeBand.XL, 25, null)];
        foreach (var band in bands.Where(b => sizes.All(s => s.MaSize != b.Code)))
        {
            var size = new HeSoCanNang { MaSize = band.Code, CanNangTu = band.From, CanNangDen = band.To };
            sizes.Add(size);
            context.HeSoCanNangSet.Add(size);
        }
        await context.SaveChangesAsync(cancellationToken);

        (string Name, string Type, int Minutes, decimal[] Prices)[] samples =
        [
            ("Tắm sấy vệ sinh", "SPA", 60, [150000, 200000, 250000, 300000]),
            ("Cắt tỉa tạo kiểu", "SPA_ADVANCED", 90, [250000, 350000, 450000, 600000]),
            ("Vệ sinh tai", "SPA", 30, [50000, 70000, 90000, 110000])
        ];
        foreach (var sample in samples)
        {
            var service = await context.DichVuSet.SingleOrDefaultAsync(s => s.TenDichVu == sample.Name, cancellationToken);
            if (service is null)
            {
                service = new DichVu { TenDichVu = sample.Name, LoaiDichVu = sample.Type, ThoiLuongPhut = sample.Minutes,
                    IsActive = true, YeuCauTiemChung = false };
                context.DichVuSet.Add(service);
                await context.SaveChangesAsync(cancellationToken);
            }
            var pricedSizes = await context.BangGiaSizeSet.Where(p => p.DichVuId == service.Id)
                .Select(p => p.SizeId).ToListAsync(cancellationToken);
            foreach (var size in sizes.Where(s => !pricedSizes.Contains(s.Id)))
                context.BangGiaSizeSet.Add(new BangGiaSize { DichVuId = service.Id, SizeId = size.Id,
                    DonGia = sample.Prices[(int)size.MaSize] });
        }
        await context.SaveChangesAsync(cancellationToken);
        await SeedRoomAndComboAsync(context, sizes, cancellationToken);
    }

    private static async Task SeedRoomAndComboAsync(PetSpaDbContext context, List<HeSoCanNang> sizes,
        CancellationToken cancellationToken)
    {
        foreach (var (name, price, capacity) in new[]
        {
            ("Phòng Tiêu Chuẩn", 250000m, 5), ("Phòng VIP", 450000m, 3)
        })
        {
            if (!await context.LoaiPhongSet.AnyAsync(x => x.TenPhong == name, cancellationToken))
                context.LoaiPhongSet.Add(new LoaiPhong { TenPhong = name, GiaMoiDem = price, SoLuongPhong = capacity, IsActive = true });
        }
        var combo = await context.ComboSet.SingleOrDefaultAsync(x => x.TenCombo == "Combo Tắm Cắt Toàn Diện", cancellationToken);
        if (combo is null)
        {
            combo = new Combo { TenCombo = "Combo Tắm Cắt Toàn Diện", IsActive = true };
            context.ComboSet.Add(combo);
            await context.SaveChangesAsync(cancellationToken);
        }
        var componentIds = await context.DichVuSet.Where(x => x.TenDichVu == "Tắm sấy vệ sinh" || x.TenDichVu == "Cắt tỉa tạo kiểu")
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var existingComponents = await context.ChiTietComboSet.Where(x => x.ComboId == combo.Id)
            .Select(x => x.DichVuId).ToListAsync(cancellationToken);
        foreach (var id in componentIds.Where(id => !existingComponents.Contains(id)))
            context.ChiTietComboSet.Add(new ChiTietCombo { ComboId = combo.Id, DichVuId = id });
        var pricedSizes = await context.BangGiaSizeSet.Where(x => x.ComboId == combo.Id)
            .Select(x => x.SizeId).ToListAsync(cancellationToken);
        decimal[] prices = [350000m, 480000m, 620000m, 800000m];
        foreach (var size in sizes.Where(x => !pricedSizes.Contains(x.Id)))
            context.BangGiaSizeSet.Add(new BangGiaSize { ComboId = combo.Id, SizeId = size.Id, DonGia = prices[(int)size.MaSize] });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedScheduleAsync(PetSpaDbContext context, NhanVien admin, NhanVien care,
        DateOnly date, CancellationToken cancellationToken)
    {
        var window = await context.DotDangKyCaSet.FirstOrDefaultAsync(w => w.TuNgay == date && w.DenNgay == date, cancellationToken);
        if (window is null)
        {
            var midnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            window = new DotDangKyCa { TuNgay = date, DenNgay = date, MoAt = midnight.AddDays(-7),
                DongAt = midnight.AddDays(-1), TaoBoiId = admin.Id };
            context.DotDangKyCaSet.Add(window);
            await context.SaveChangesAsync(cancellationToken);
        }
        (string Name, TimeOnly Start, TimeOnly End)[] samples =
            [("Ca Sáng", new(8, 0), new(12, 0)), ("Ca Chiều", new(13, 30), new(17, 30))];
        foreach (var sample in samples)
        {
            var shift = await context.CaLamViecSet.SingleOrDefaultAsync(s => s.TenCa == sample.Name, cancellationToken);
            if (shift is null)
            {
                shift = new CaLamViec { TenCa = sample.Name, GioBatDau = sample.Start, GioKetThuc = sample.End,
                    SucChuaPet = 10, TranPhut = 240, IsActive = true };
                context.CaLamViecSet.Add(shift);
                await context.SaveChangesAsync(cancellationToken);
            }
            if (!await context.KhungGioDatLichSet.AnyAsync(s => s.Ngay == date && s.CaLamViecId == shift.Id, cancellationToken))
                context.KhungGioDatLichSet.Add(new KhungGioDatLich { Ngay = date, CaLamViecId = shift.Id,
                    MoNhanKhach = true, TongSlot = shift.SucChuaPet, TranPhut = shift.TranPhut });
            if (!await context.DangKyCaSet.AnyAsync(r => r.NhanVienId == care.Id && r.Ngay == date && r.CaLamViecId == shift.Id, cancellationToken))
                context.DangKyCaSet.Add(new DangKyCa { Ngay = date, CaLamViecId = shift.Id, DotId = window.Id,
                    NhanVienId = care.Id, TrangThai = ShiftRegistrationState.APPROVED, DuyetBoiId = admin.Id, DuyetAt = DateTimeOffset.UtcNow });
        }
        await context.SaveChangesAsync(cancellationToken);
    }
}
