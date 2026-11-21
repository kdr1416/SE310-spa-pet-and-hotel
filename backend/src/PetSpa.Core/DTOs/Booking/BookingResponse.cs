using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Core.DTOs.Booking;

public sealed record BookingResponse(
    int MaDon,
    string TrackingCode,
    string TrangThai,
    DateTimeOffset BatDauDuKien,
    DateTimeOffset KetThucDuKien,
    Contact LienHe,
    decimal TongTamTinh,
    IReadOnlyList<BookingPet> ThuCung,
    object? TienCoc,
    InvoiceResponseDto? HoaDon,
    string EmailDelivery);
