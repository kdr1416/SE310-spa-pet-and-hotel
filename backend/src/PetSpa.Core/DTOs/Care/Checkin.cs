using System.ComponentModel.DataAnnotations;
using PetSpa.Core.Enums;

namespace PetSpa.Core.DTOs.Care;

public class Checkin
{
    [Required]
    public Guid RequestId { get; set; }

    [Range(0.01, 200)]
    public decimal CanNangThucTe { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(20)]
    public List<string> TinhTrangLong { get; set; } = new();

    [Range(1, int.MaxValue)]
    public int MaNhanVien { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string GhiChuSucKhoe { get; set; } = string.Empty;

    [Required]
    public PriceDecision? QuyetDinhGia { get; set; }

    public bool XacNhanKhach { get; set; }

    [StringLength(1000, MinimumLength = 1)]
    public string? NoiDungThuGon { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaHoSoTiemChung { get; set; }

    public bool? XacNhanGiayHopLe { get; set; }
}
