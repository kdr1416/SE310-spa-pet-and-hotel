using System.ComponentModel.DataAnnotations;

namespace PetSpa.Core.DTOs.Care;

public enum CareActionType
{
    START = 0,
    PAUSE = 1,
    RESUME = 2,
    READY = 3
}

public class CareAction
{
    [Required]
    public Guid RequestId { get; set; }

    [Required]
    public CareActionType? HanhDong { get; set; }

    [StringLength(2000, MinimumLength = 1)]
    public string? GhiChu { get; set; }

    [Range(1, int.MaxValue)]
    public int? MaNgoaiLeAnh { get; set; }
}
