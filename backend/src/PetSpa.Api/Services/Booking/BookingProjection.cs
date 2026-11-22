using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Services.Billing;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public static class BookingProjection
{
    internal static IQueryable<DonDatLich> IncludeDetails(IQueryable<DonDatLich> bookings) =>
        bookings
            .Include(booking => booking.KhungGio)
            .Include(booking => booking.ChiTietDonDonDatLichItems).ThenInclude(pet => pet.NhanVien)
            .Include(booking => booking.ChiTietDonDonDatLichItems).ThenInclude(pet => pet.DichVuTrongDonChiTietDonItems)
                .ThenInclude(service => service.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .Include(booking => booking.ChiTietDonDonDatLichItems).ThenInclude(pet => pet.NhatKyChamSocChiTietDonItems)
                .ThenInclude(log => log.NhanVien)
            .Include(booking => booking.HoaDonDonDatLichItems).ThenInclude(invoice => invoice.TaoBoi)
            .Include(booking => booking.HoaDonDonDatLichItems).ThenInclude(invoice => invoice.CtHoaDonHoaDonItems)
            .Include(booking => booking.TienCocDonDatLich).ThenInclude(deposit => deposit!.GiaoDichThuTienCocItems)
            .Include(booking => booking.TienCocDonDatLich).ThenInclude(deposit => deposit!.HoanCocTienCocItems)
            .Include(booking => booking.PhuPhiDonDatLichItems)
            .AsSplitQuery();

    internal static BookingResponse ProjectCurrent(DonDatLich booking, IConfiguration configuration, bool includeInternalDetails = false)
    {
        var invoice = booking.HoaDonDonDatLichItems.OrderBy(invoice => invoice.TrangThai == InvoiceState.REVOKED)
            .ThenByDescending(invoice => invoice.Id).FirstOrDefault();
        return Project(booking, invoice is null ? null : InvoiceProjection.Project(invoice, configuration, includeInternalDetails: false), includeInternalDetails);
    }

    public static BookingResponse Project(DonDatLich booking, InvoiceResponseDto? invoice = null, bool includeInternalDetails = false)
    {
        var amounts = DepositAccounting.Calculate(booking);
        var deposit = booking.TienCocDonDatLich;
        var depositDto = deposit is null ? null : new DepositDto(deposit.Id, booking.Id,
            deposit.TrangThaiYeuCau.ToString(), deposit.CoSoTinhSnapshot, deposit.TyLe,
            deposit.SoTienYeuCau, deposit.HetHanAt, amounts.ThucNhan, amounts.KhauTru, amounts.GiuLai,
            amounts.DanhHoan, amounts.DaHoan, deposit.HoanCocTienCocItems.OrderBy(x => x.Id)
                .Select(x => new RefundDto(x.Id, deposit.Id, x.SoTien, x.LyDo, x.TrangThai.ToString(),
                    x.HoanAt, includeInternalDetails ? MaskReference(x.ThamChieu) : null)).ToArray());
        var pets = booking.ChiTietDonDonDatLichItems.OrderBy(pet => pet.Id).Select(pet => new BookingPet
        {
            MaChiTietDon = pet.Id,
            MaThuCung = pet.ThuCungId,
            TenThuCung = pet.TenPetSnapshot,
            TrangThai = pet.TrangThaiChamSoc.ToString(),
            CanNangChot = pet.CanNangChot,
            SizeThucTe = pet.SizeThucTe?.ToString(),
            SizeChot = pet.SizeChot?.ToString(),
            TinhTrangLong = ReadFurConditions(pet.TinhTrangLong),
            MaLoaiPhong = pet.LoaiPhongId,
            TenNhanVienPhuTrach = pet.NhanVien?.TenNhanVien,
            NhanSom = pet.NhanSom,
            BanGiaoAt = pet.BanGiaoAt,
            TraLaiAt = pet.TraLaiAt,
            NhatKy = pet.NhatKyChamSocChiTietDonItems.OrderBy(log => log.GhiNhanAt).ThenBy(log => log.Id)
                .Select(log => new CareLog
                {
                    MaNhatKy = log.Id,
                    LoaiNhatKy = log.LoaiNhatKy,
                    GhiChu = log.LoaiNhatKy == "CARE_LOG" ? log.GhiChu : null,
                    CanNangTheoDoi = log.CanNangGhiNhan,
                    TenTacGia = log.NhanVien?.TenNhanVien ?? string.Empty,
                    GhiNhanAt = log.GhiNhanAt
                }).ToList(),
            DichVu = pet.DichVuTrongDonChiTietDonItems.OrderBy(service => service.Id).Select(service => new OrderService
            {
                MaDichVuTrongDon = service.Id,
                MaDichVu = service.DichVuId,
                MaCombo = service.ComboId,
                MaKhungGio = service.KhungGioId,
                TenDichVu = service.TenSnapshot,
                TrangThai = service.TrangThai.ToString(),
                SizeChot = service.SizeChot?.ToString(),
                GiaChot = service.GiaChot,
                ThanhPhan = service.ThanhPhanDichVuTrongDonDichVuTrongDonItems.OrderBy(part => part.Id)
                    .Select(part => new Component
                    {
                        MaThanhPhan = part.Id,
                        MaDichVu = part.DichVuId,
                        TenDichVu = part.TenSnapshot,
                        TrangThai = part.TrangThai.ToString(),
                        HoanTatAt = part.HoanTatAt
                    }).ToList()
            }).ToList()
        }).ToArray();

        return new BookingResponse(booking.Id, booking.TrackingCode, booking.TrangThai.ToString(),
            booking.BatDauDuKien, booking.KetThucDuKien,
            new Contact(booking.TenNguoiDat, booking.SoDienThoaiSnapshot, booking.EmailSnapshot),
            booking.TongTamTinh, pets, depositDto, invoice, booking.EmailDelivery ?? "FAILED");
    }

    private static string? MaskReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (reference.Length <= 4) return "***";
        var tail = reference[^4..];
        return tail.All(char.IsAsciiLetterOrDigit) ? "***" + tail : "***";
    }

    private static List<string>? ReadFurConditions(string? conditions)
    {
        if (string.IsNullOrWhiteSpace(conditions))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(conditions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
