using System.Text.Json;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public sealed class BillingSettlementHelper : IBillingSettlementHelper
{
    public void SettleCancelledPetInMemory(DonDatLich booking, ChiTietDon pet,
        IReadOnlySet<int> completedComponentIds, int nightsStayed, DateTimeOffset now)
    {
        if (!booking.ChiTietDonDonDatLichItems.Contains(pet) || pet.DonDatLichId != booking.Id)
            throw new ArgumentException("Thú cưng không thuộc đơn.");
        if (nightsStayed < 0 || nightsStayed > (pet.SoDemChot ?? 0) || !pet.LoaiPhongId.HasValue && nightsStayed != 0)
            throw new ArgumentException("Số đêm đã ở không hợp lệ.");
        var completed = pet.DichVuTrongDonChiTietDonItems.SelectMany(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .Where(p => p.TrangThai == OrderServiceState.HOAN_TAT).Select(p => p.Id).ToHashSet();
        if (!completedComponentIds.SetEquals(completed)) throw new ArgumentException("Danh sách phần đã làm không khớp dữ liệu server.");

        // Resolve and validate every price first: a later missing snapshot must not partially cancel this pet.
        var settlements = pet.DichVuTrongDonChiTietDonItems.ToDictionary(line => line, line => SettleLine(booking, pet, line));
        var total = 0m;
        foreach (var current in booking.ChiTietDonDonDatLichItems)
        {
            foreach (var line in current.DichVuTrongDonChiTietDonItems)
            {
                if (ReferenceEquals(current, pet)) total += settlements[line].Price;
                else if (line.TrangThai != OrderServiceState.HUY) total += LinePrice(booking, current, line);
            }
            total += RoomTotal(current, ReferenceEquals(current, pet) ? nightsStayed : null);
        }
        total += booking.PhuPhiDonDatLichItems.Where(fee => fee.HuyAt is null).Sum(fee => ValidPrice(fee.SoTien));
        ValidPrice(total);
        foreach (var (line, settlement) in settlements)
        {
            line.GiaChot = settlement.Price;
            line.TrangThai = settlement.State;
            line.UpdatedAt = now;
            if (settlement.State == OrderServiceState.HUY) line.HuyAt ??= now;
            else line.HoanTatAt ??= now;
            foreach (var component in line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Where(p => p.TrangThai != OrderServiceState.HOAN_TAT))
            {
                component.TrangThai = OrderServiceState.HUY;
                component.UpdatedAt = now;
            }
        }
        pet.SoDemDaO = nightsStayed;
        pet.UpdatedAt = now;
        booking.TongTamTinh = total;
        booking.UpdatedAt = now;
    }

    public decimal CalculateObligatedDepositAmount(DonDatLich booking)
    {
        if (booking.TienCocDonDatLich is null) return 0m;
        var amounts = DepositAccounting.Calculate(booking);
        if (booking.TrangThai == BookingState.CANCELLED && booking.ChiTietDonDonDatLichItems.All(p => p.CheckInAt is null))
            return amounts.GiuLai;
        var invoice = booking.HoaDonDonDatLichItems.SingleOrDefault(x => x.TrangThai != InvoiceState.REVOKED);
        return invoice?.CocKhauTru ?? Math.Min(Math.Max(0m, amounts.ThucNhan - amounts.DaHoan), Math.Max(0m, booking.TongTamTinh));
    }

    public void TryTransitionBookingTerminalState(DonDatLich booking, DateTimeOffset now)
    {
        if (booking.TrangThai is BookingState.CANCELLED or BookingState.COMPLETED || booking.ChiTietDonDonDatLichItems.Count == 0) return;
        var pets = booking.ChiTietDonDonDatLichItems;
        var amounts = DepositAccounting.Calculate(booking);
        var invoice = booking.HoaDonDonDatLichItems.SingleOrDefault(x => x.TrangThai != InvoiceState.REVOKED);
        if (pets.Any(p => p.TrangThaiChamSoc != PetCareState.CANCELLED))
        {
            if (booking.TrangThai != BookingState.IN_PROGRESS || invoice?.TrangThai != InvoiceState.PAID ||
                pets.Any(p => p.TrangThaiChamSoc != PetCareState.CANCELLED && p.TrangThaiChamSoc != PetCareState.DONE ||
                    p.TrangThaiChamSoc == PetCareState.CANCELLED && p.TraLaiAt is null)) return;
            booking.TrangThai = BookingState.COMPLETED;
            booking.HoanTatAt ??= now;
        }
        else
        {
            if (booking.TrangThai is BookingState.PENDING or BookingState.CONFIRMED)
            {
                if (pets.Any(p => p.CheckInAt.HasValue) || invoice?.TrangThai == InvoiceState.UNPAID) return;
            }
            else
            {
                if (booking.TrangThai != BookingState.IN_PROGRESS || pets.Any(p => p.TraLaiAt is null) ||
                    (booking.TongTamTinh > 0m ? invoice?.TrangThai != InvoiceState.PAID : invoice?.TrangThai == InvoiceState.UNPAID) ||
                    booking.TienCocDonDatLich?.HoanCocTienCocItems.Any(x => x.TrangThai == RefundState.CHO_HOAN) == true ||
                    amounts.ThucNhan - amounts.KhauTru - amounts.GiuLai - amounts.DanhHoan > 0m) return;
            }
            booking.TrangThai = BookingState.CANCELLED;
            booking.HuyAt ??= now;
            // LoaiHuy remains null for E47: terminalization is not whole-booking forfeiture.
        }
        booking.UpdatedAt = now;
    }

    private static (decimal Price, OrderServiceState State) SettleLine(DonDatLich booking, ChiTietDon pet, DichVuTrongDon line)
    {
        if (!line.ComboId.HasValue)
            return line.TrangThai == OrderServiceState.HOAN_TAT ? (LinePrice(booking, pet, line), OrderServiceState.HOAN_TAT) : (0m, OrderServiceState.HUY);
        var parts = line.ThanhPhanDichVuTrongDonDichVuTrongDonItems;
        if (parts.Count == 0) throw new InvalidOperationException("Chưa nạp thành phần combo.");
        var done = parts.Where(p => p.TrangThai == OrderServiceState.HOAN_TAT).ToArray();
        if (done.Length == 0) return (0m, OrderServiceState.HUY);
        if (done.Length == parts.Count) return (LinePrice(booking, pet, line), OrderServiceState.HOAN_TAT);
        var size = pet.SizeChot ?? throw new InvalidOperationException("Thiếu size đã chốt cho combo đang làm.");
        return (done.Sum(p => SnapshotPrice(p.GiaLeSizeSnapshot, size)), OrderServiceState.HOAN_TAT);
    }

    private static decimal LinePrice(DonDatLich booking, ChiTietDon pet, DichVuTrongDon line)
    {
        if (line.GiaChot.HasValue) return ValidPrice(line.GiaChot.Value);
        var size = line.SizeChot ?? pet.SizeChot ?? FrozenSize(booking, pet);
        return SnapshotPrice(line.BangGiaSizeSnapshot, size);
    }

    private static decimal RoomTotal(ChiTietDon pet, int? targetNights)
    {
        if (!pet.LoaiPhongId.HasValue) return 0m;
        var nights = targetNights ?? (pet.TrangThaiChamSoc == PetCareState.CANCELLED ? pet.SoDemDaO ?? 0 :
            pet.SoDemChot ?? throw new InvalidOperationException("Thiếu số đêm cam kết."));
        if (nights == 0) return 0m;
        if (nights < 0) throw new InvalidOperationException("Số đêm âm.");
        return ValidPrice(pet.GiaPhongChot ?? pet.GiaPhongCamKet ?? throw new InvalidOperationException("Thiếu giá phòng snapshot.")) * nights;
    }

    private static SizeBand FrozenSize(DonDatLich booking, ChiTietDon pet)
    {
        try
        {
            using var policy = JsonDocument.Parse(booking.ChinhSachSnapshot);
            if (policy.RootElement.ValueKind == JsonValueKind.Object && policy.RootElement.TryGetProperty("thuCung", out var pets) && pets.ValueKind == JsonValueKind.Array)
                foreach (var saved in pets.EnumerateArray())
                    if (saved.TryGetProperty("maChiTietDon", out var id) && id.TryGetInt32(out var value) && value == pet.Id &&
                        saved.TryGetProperty("sizeCamKet", out var size) && size.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<SizeBand>(size.GetString(), out var band) && Enum.IsDefined(band)) return band;
        }
        catch (JsonException exception) { throw new InvalidOperationException("Snapshot chính sách không hợp lệ.", exception); }
        throw new InvalidOperationException("Thiếu size cam kết trong snapshot.");
    }

    private static decimal SnapshotPrice(string snapshot, SizeBand size)
    {
        try
        {
            using var prices = JsonDocument.Parse(snapshot);
            if (prices.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var price in prices.RootElement.EnumerateObject())
                    if (price.Name.Equals(size.ToString(), StringComparison.OrdinalIgnoreCase) && price.Value.TryGetDecimal(out var amount))
                        return ValidPrice(amount);
        }
        catch (JsonException exception) { throw new InvalidOperationException("Snapshot giá không hợp lệ.", exception); }
        throw new InvalidOperationException("Thiếu giá size trong snapshot.");
    }

    private static decimal ValidPrice(decimal price) => price >= 0m && price <= 99_999_999_999_999m && decimal.Truncate(price) == price
        ? price : throw new InvalidOperationException("Số tiền snapshot không hợp lệ.");
}
