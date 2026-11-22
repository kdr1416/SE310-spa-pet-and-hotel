using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs.Billing;

public sealed record DepositDto(int MaTienCoc, int MaDon, string TrangThaiYeuCau, decimal CoSoTinh,
    decimal TyLe, decimal SoTienYeuCau, DateTimeOffset HetHanAt, decimal ThucNhan, decimal KhauTru,
    decimal GiuLai, decimal DanhHoan, decimal DaHoan, IReadOnlyList<RefundDto> HoanCoc);
public sealed record RefundDto(int MaHoanCoc, int MaTienCoc, decimal SoTien, string LyDo,
    string TrangThai, DateTimeOffset? HoanAt, string? ThamChieu);
public sealed record PageDeposit(IReadOnlyList<DepositDto> Items, int Trang, int KichThuocTrang, int TongSo);
public sealed record PageRefund(IReadOnlyList<RefundDto> Items, int Trang, int KichThuocTrang, int TongSo);

public sealed class ReceiptCreate : ContractCommand
{
    [JsonRequired, VndAmount]
    public decimal SoTien { get; init; }
    [JsonRequired, Required, RegularExpression("CASH|BANK_TRANSFER")]
    public string PhuongThuc { get; init; } = string.Empty;
    [JsonRequired, JsonConverter(typeof(ExplicitOffsetInstantConverter))]
    public DateTimeOffset GiaoDichAt { get; init; }
    [StringLength(150, MinimumLength = 1)]
    public string? MaGiaoDich { get; init { field = value ?? throw new JsonException("MaGiaoDich không được null."); } }
    [StringLength(100, MinimumLength = 1)]
    public string? NganHang { get; init { field = value ?? throw new JsonException("NganHang không được null."); } }
    [StringLength(2000, MinimumLength = 1)]
    public string? GhiChu { get; init { field = value ?? throw new JsonException("GhiChu không được null."); } }
}

public sealed class RefundCreate : ContractCommand
{
    [JsonRequired, VndAmount]
    public decimal SoTien { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

public sealed class RefundConfirm : ContractCommand
{
    [JsonRequired, Required, RegularExpression("CASH|BANK_TRANSFER")]
    public string PhuongThuc { get; init; } = string.Empty;
    [JsonRequired, JsonConverter(typeof(ExplicitOffsetInstantConverter))]
    public DateTimeOffset HoanAt { get; init; }
    [JsonRequired, Required, StringLength(150, MinimumLength = 1)]
    public string ThamChieu { get; init; } = string.Empty;
    [StringLength(2000, MinimumLength = 1)]
    public string? BangChungFileKey { get; init { field = value ?? throw new JsonException("BangChungFileKey không được null."); } }
}
