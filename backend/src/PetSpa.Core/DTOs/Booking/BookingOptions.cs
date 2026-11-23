using System.ComponentModel.DataAnnotations;

namespace PetSpa.Core.DTOs.Booking;

public sealed class BookingOptionsQuery
{
    [Required]
    public DateOnly? Ngay { get; set; }
}

public sealed class BookingOptions
{
    public DateOnly Ngay { get; set; }
    public List<BookingShiftOption> Ca { get; set; } = [];
}

public sealed class BookingShiftOption
{
    public int MaCa { get; set; }
    public string TenCa { get; set; } = string.Empty;
    public TimeOnly GioBatDau { get; set; }
    public TimeOnly GioKetThuc { get; set; }
    public int? MaKhungGio { get; set; }
    public bool MoNhanKhach { get; set; }
}
