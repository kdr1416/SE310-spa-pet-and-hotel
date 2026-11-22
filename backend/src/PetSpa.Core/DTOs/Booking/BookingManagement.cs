using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Core.DTOs.Booking;

public sealed class BookingQuery
{
    [Range(1, int.MaxValue)]
    public int Trang { get; init; } = 1;

    [Range(1, 100)]
    public int KichThuocTrang { get; init; } = 20;

    public DateOnly? TuNgay { get; init; }
    public DateOnly? DenNgay { get; init; }
    public string? TrangThai { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BookingCommand(Guid RequestId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelBooking(
    Guid RequestId,
    [Required] string LoaiHuy,
    [Required, StringLength(1000)] string LyDo);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrackingPair(
    [Required, RegularExpression("[0-9]{10}")] string SoDienThoai,
    [Required, StringLength(8, MinimumLength = 8)] string TrackingCode);

public sealed record PageBooking(IReadOnlyList<BookingResponse> Items, int Trang, int KichThuocTrang, int TongSo);

public sealed record TrackingResponse(
    int MaDon,
    string TrackingCode,
    string TrangThai,
    DateTimeOffset BatDauDuKien,
    DateTimeOffset KetThucDuKien,
    decimal TongTamTinh,
    IReadOnlyList<BookingPet> ThuCung,
    InvoiceResponseDto? HoaDon,
    DepositDto? TienCoc,
    IReadOnlyList<string> HanhDongChoPhep);
