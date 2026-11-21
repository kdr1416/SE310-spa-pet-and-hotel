using System.Text.Json.Serialization;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Enums;

namespace PetSpa.Core.DTOs.Booking;

public class Contact
{
    public string HoTen { get; set; } = string.Empty;
    public string SoDienThoai { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class PetSelection
{
    public int? MaThuCung { get; set; }
    public string? TenThuCung { get; set; }
    public string? LoaiPet { get; set; }
    public string? Giong { get; set; }
    public decimal CanNangKhaiBao { get; set; }
    public string? LoaiLong { get; set; }
    public string? GhiChuDiUng { get; set; }
    public int? MaLoaiPhong { get; set; }
    public List<ServiceSelection> DichVu { get; set; } = new();
    public string? VaccinationFileKey { get; set; }
    public int? MaHoSoTiemChung { get; set; }
}

public class BookingCreate
{
    public Guid RequestId { get; set; }
    public Contact LienHe { get; set; } = new();
    public int MaKhungGio { get; set; }
    public DateOnly NgayTra { get; set; }
    public List<PetSelection> ThuCung { get; set; } = new();
    public string? GhiChu { get; set; }
    public string? BookingProof { get; set; }
    public string? PetProof { get; set; }
    public int? MaKhachHang { get; set; }
}

public class BookingResponse
{
    public int MaDon { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string TrangThai { get; set; } = string.Empty;
    public DateTimeOffset BatDauDuKien { get; set; }
    public DateTimeOffset KetThucDuKien { get; set; }
    public Contact LienHe { get; set; } = new();
    public decimal TongTamTinh { get; set; }
    public List<BookingPet> ThuCung { get; set; } = new();
    public object? TienCoc { get; set; }
    public object? HoaDon { get; set; }
    public string EmailDelivery { get; set; } = "SENT";
}

public class BookingQuery
{
    public int Trang { get; set; } = 1;
    public int KichThuocTrang { get; set; } = 20;
    public DateOnly? TuNgay { get; set; }
    public DateOnly? DenNgay { get; set; }
    public string? TrangThai { get; set; }
}

public class PageBooking
{
    public List<BookingResponse> Items { get; set; } = new();
    public int TongSo { get; set; }
    public int Trang { get; set; }
    public int KichThuocTrang { get; set; }
    public int TongSoTrang { get; set; }
}

public class ConfirmBookingCommand
{
    public Guid RequestId { get; set; }
}

public class CancelBookingCommand
{
    public Guid RequestId { get; set; }
    public CancellationType LoaiHuy { get; set; } = CancellationType.STAFF_REJECTED;
    public string LyDo { get; set; } = string.Empty;
}

public sealed record BookingServiceResult(
    PetSpa.Core.Common.ApiResponse<BookingResponse> Response,
    bool IsReplay = false,
    int StatusCode = 200
);
