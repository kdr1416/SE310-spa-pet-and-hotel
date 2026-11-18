using System.ComponentModel.DataAnnotations;

namespace PetSpa.Core.DTOs.Care;

public class CareLogCreate
{
    [Required]
    public Guid RequestId { get; set; }

    [StringLength(2000, MinimumLength = 1)]
    public string? GhiChu { get; set; }

    [Range(0.01, 999.99)]
    public decimal? CanNangTheoDoi { get; set; }
}
