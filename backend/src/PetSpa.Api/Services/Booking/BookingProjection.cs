using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;

namespace PetSpa.Api.Services.Booking;

public static class BookingProjection
{
    public static BookingResponse Project(DonDatLich booking, InvoiceResponseDto? invoice = null)
    {
        var pets = booking.ChiTietDonDonDatLichItems.OrderBy(pet => pet.Id).Select(pet => new BookingPet
        {
            MaChiTietDon = pet.Id,
            MaThuCung = pet.ThuCungId,
            TenThuCung = pet.TenPetSnapshot,
            TrangThai = pet.TrangThaiChamSoc.ToString(),
            CanNangChot = pet.CanNangChot,
            SizeThucTe = pet.SizeThucTe?.ToString(),
            SizeChot = pet.SizeChot?.ToString(),
            MaLoaiPhong = pet.LoaiPhongId,
            TenNhanVienPhuTrach = pet.NhanVien?.TenNhanVien,
            NhanSom = pet.NhanSom,
            BanGiaoAt = pet.BanGiaoAt,
            TraLaiAt = pet.TraLaiAt,
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
            booking.TongTamTinh, pets, null, invoice, booking.EmailDelivery ?? "FAILED");
    }
}
