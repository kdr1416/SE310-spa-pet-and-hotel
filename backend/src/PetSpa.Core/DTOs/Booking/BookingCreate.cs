using System.Text.Json.Serialization;
using PetSpa.Core.DTOs.Availability;

namespace PetSpa.Core.DTOs.Booking;

public sealed record BookingCreate(
    Guid RequestId,
    Contact LienHe,
    int MaKhungGio,
    DateOnly NgayTra,
    List<PetSelection> ThuCung,
    string? GhiChu = null,
    string? BookingProof = null,
    string? PetProof = null,
    int? MaKhachHang = null);

public sealed record Contact(string HoTen, string SoDienThoai, string Email);

public sealed record PetSelection(
    decimal CanNangKhaiBao,
    List<ServiceSelection> DichVu,
    int? MaThuCung = null,
    string? TenThuCung = null,
    string? LoaiPet = null,
    string? Giong = null,
    string? LoaiLong = null,
    string? GhiChuDiUng = null,
    int? MaLoaiPhong = null,
    string? VaccinationFileKey = null,
    int? MaHoSoTiemChung = null);
