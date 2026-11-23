namespace PetSpa.Core.DTOs.Care;

public sealed record PageBookingPet(IReadOnlyList<BookingPet> Items, int Trang, int KichThuocTrang, int TongSo);
