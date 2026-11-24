using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Services.Care;

public class CheckinService(PetSpaDbContext dbContext, CareOperation operation) : ICheckinService
{
    private const string Permission = "CHECKIN_PERFORM";
    private const string ReassignOperation = "CARE_REASSIGN";

    public async Task<AssignmentCandidatesResult> GetAssignmentCandidatesAsync(
        int actorId,
        ShiftQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = await operation.FindEmployeeWithGrantAsync(actorId, Permission, cancellationToken);
        if (actor is null)
            return new AssignmentCandidatesResult(403, Message: "Bạn không có quyền xem ứng viên phân công.", ErrorCode: "FORBIDDEN");

        var errors = new List<ApiErrorItem>();
        if (query.Ngay == default)
            errors.Add(new ApiErrorItem { Field = "ngay", Message = "Ngày là bắt buộc." });
        if (query.MaCa <= 0)
            errors.Add(new ApiErrorItem { Field = "maCa", Message = "maCa phải lớn hơn 0." });
        if (errors.Count > 0)
            return new AssignmentCandidatesResult(400, Message: "Dữ liệu không hợp lệ.", ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var shiftExists = await dbContext.CaLamViecSet.AsNoTracking()
            .AnyAsync(shift => shift.Id == query.MaCa, cancellationToken);
        if (!shiftExists)
            return new AssignmentCandidatesResult(404, Message: "Không tìm thấy ca làm việc.", ErrorCode: "NOT_FOUND");

        var candidates = await dbContext.NhanVienSet.AsNoTracking()
            .Where(employee => employee.IsActive
                && employee.NhanVienQuyenNhanVienItems.Any(grant => grant.MaQuyen == "CARE_UPDATE")
                && employee.DangKyCaNhanVienItems.Any(shift =>
                    shift.Ngay == query.Ngay &&
                    shift.CaLamViecId == query.MaCa &&
                    shift.TrangThai == ShiftRegistrationState.APPROVED))
            .OrderBy(employee => employee.Id)
            .Select(employee => new Employee
            {
                MaNhanVien = employee.Id,
                TenNhanVien = employee.TenNhanVien,
                TenDangNhap = employee.TenDangNhap,
                VaiTro = employee.VaiTro.ToString(),
                Quyen = employee.NhanVienQuyenNhanVienItems.Select(grant => grant.MaQuyen).OrderBy(code => code).ToList(),
                DangHoatDong = employee.IsActive
            })
            .ToListAsync(cancellationToken);

        return new AssignmentCandidatesResult(200, candidates, "Lấy danh sách kỹ thuật viên khả dụng thành công.");
    }

    public async Task<CheckinResult> ReassignAsync(
        int chiTietDonId,
        int actorId,
        AssignmentRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (chiTietDonId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (request.MaNhanVien <= 0)
            errors.Add(new ApiErrorItem { Field = "maNhanVien", Message = "maNhanVien phải lớn hơn 0." });
        if (string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Trim().Length > 1000)
            errors.Add(new ApiErrorItem { Field = "lyDo", Message = "lyDo bắt buộc và tối đa 1000 ký tự." });

        if (errors.Count > 0)
            return new CheckinResult(400, Message: "Dữ liệu phân công lại không hợp lệ.", ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var reason = request.LyDo.Trim();
        var fingerprint = CareOperation.Fingerprint(new
        {
            chiTietDonId,
            request.RequestId,
            request.MaNhanVien,
            LyDo = reason
        });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, Permission, ReassignOperation, traceId, fingerprint);
        return ToCheckinResult(await operation.ExecuteAsync<BookingPet>(command, "Phân công lại nhân viên chăm sóc thành công.", null,
            (actor, now, _, _, ct) => ReassignPetAsync(chiTietDonId, actor, request.MaNhanVien, reason, now, ct),
            cancellationToken));
    }

    private async Task<CareResult<BookingPet>> ReassignPetAsync(
        int petId,
        NhanVien actor,
        int newEmployeeId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pet = await CareProjection.IncludeDetails(dbContext.ChiTietDonSet)
            .Include(p => p.DonDatLich).ThenInclude(booking => booking.HoaDonDonDatLichItems)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        if (dbContext.Database.IsRelational())
        {
            var slotIds = pet.DichVuTrongDonChiTietDonItems
                .Where(line => line.TrangThai != OrderServiceState.HUY)
                .Select(line => line.KhungGioId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            if (slotIds.Length > 0)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM khung_gio_dat_lich WHERE id = ANY({slotIds}) ORDER BY id FOR UPDATE",
                    cancellationToken);
            }

            var todayHcm = CareOperation.HcmDate(now);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM dang_ky_ca WHERE nhan_vien_id = {newEmployeeId} AND ngay = {todayHcm} ORDER BY id FOR UPDATE",
                cancellationToken);
        }

        if (pet.DonDatLich.HoaDonDonDatLichItems.Any(invoice => invoice.TrangThai != InvoiceState.REVOKED))
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực, không thể phân công lại.");

        if (pet.TrangThaiChamSoc is not (PetCareState.WAITING or PetCareState.IN_PROGRESS or PetCareState.PAUSED or PetCareState.READY))
            return CareResult<BookingPet>.InvalidTransition("Chỉ có thể phân công lại khi thú cưng đang ở trạng thái WAITING, IN_PROGRESS, PAUSED hoặc READY.");

        var assignee = await operation.FindEmployeeWithGrantAsync(newEmployeeId, "CARE_UPDATE", cancellationToken);
        if (assignee is null)
            return AssignmentUnavailable("Nhân viên được phân công phải hoạt động và có quyền CARE_UPDATE.");

        if (!await operation.HasCurrentShiftAsync(assignee.Id, now, cancellationToken))
            return AssignmentUnavailable("Nhân viên được phân công không có ca làm việc được duyệt tại thời điểm hiện tại.");

        pet.NhanVienId = assignee.Id;
        pet.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CareResult<BookingPet>(
            200,
            CareProjection.Pet(pet, actor, assignee.TenNhanVien),
            AssigneeId: assignee.Id,
            OverrideReason: reason);
    }

    public async Task<CheckinResult> CheckinAsync(
        int chiTietDonId,
        int actorId,
        Checkin request,
        CheckinPhotoInput photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateRequest(chiTietDonId, request, photo);
        if (errors.Count > 0)
            return new CheckinResult(400, Message: "Dữ liệu check-in không hợp lệ.", ErrorCode: "VALIDATION_FAILED", Errors: errors);

        if (CareOperation.ValidatePhoto<BookingPet>(photo.Content, "anhTiepNhan") is { } invalidPhoto)
            return ToCheckinResult(invalidPhoto);

        var normalizedFurConditions = request.TinhTrangLong.Select(code => code.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var payload = new
        {
            chiTietDonId,
            request.RequestId,
            request.CanNangThucTe,
            TinhTrangLong = normalizedFurConditions,
            request.MaNhanVien,
            GhiChuSucKhoe = request.GhiChuSucKhoe.Trim(),
            request.QuyetDinhGia,
            request.XacNhanKhach,
            NoiDungThuGon = request.NoiDungThuGon?.Trim(),
            request.MaHoSoTiemChung,
            request.XacNhanGiayHopLe
        };

        var fingerprint = CareOperation.Fingerprint(new { payload, PhotoHash = CareOperation.PhotoFingerprint(photo.Content) });
        var legacyFingerprint = CareOperation.Fingerprint(new
        {
            payload.chiTietDonId,
            payload.RequestId,
            payload.CanNangThucTe,
            payload.TinhTrangLong,
            payload.MaNhanVien,
            payload.GhiChuSucKhoe,
            payload.QuyetDinhGia,
            payload.XacNhanKhach,
            payload.NoiDungThuGon,
            payload.MaHoSoTiemChung,
            payload.XacNhanGiayHopLe,
            photo.FileName,
            photo.Length
        });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, Permission, Permission, traceId, fingerprint, legacyFingerprint);
        return ToCheckinResult(await operation.ExecuteAsync<BookingPet>(command, "Check-in thú cưng thành công.", photo.Content,
            (actor, now, path, preflight, ct) =>
                CheckInPetAsync(chiTietDonId, actor, request, normalizedFurConditions, now, path, preflight, ct),
            cancellationToken));
    }

    private async Task<CareResult<BookingPet>> CheckInPetAsync(
        int petId, NhanVien actor, Checkin request, List<string> furConditions,
        DateTimeOffset now, string? objectPath, bool preflight, CancellationToken cancellationToken)
    {
        if (await ValidateFurConditionsAsync(furConditions, cancellationToken) is { } invalidFur)
            return invalidFur;

        var pet = await CareProjection.IncludeDetails(dbContext.ChiTietDonSet)
            .Include(pet => pet.ThuCung)
            .Include(pet => pet.LoaiPhong)
            .Include(pet => pet.DonDatLich).ThenInclude(booking => booking.HoaDonDonDatLichItems)
            .Include(pet => pet.DichVuTrongDonChiTietDonItems).ThenInclude(line => line.DichVu)
            .AsSplitQuery()
            .SingleOrDefaultAsync(pet => pet.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var booking = pet.DonDatLich;
        if (booking.TrangThai is not (BookingState.CONFIRMED or BookingState.IN_PROGRESS) ||
            (booking.TrangThai == BookingState.IN_PROGRESS && !await dbContext.ChiTietDonSet.AnyAsync(other =>
                other.DonDatLichId == booking.Id && other.Id != pet.Id && other.CheckInAt != null, cancellationToken)))
            return CareResult<BookingPet>.InvalidTransition("Chỉ có thể check-in khi đơn đã xác nhận hoặc đã tiếp nhận thú cưng khác.");
        if (pet.TrangThaiChamSoc != PetCareState.NOT_ARRIVED)
            return CareResult<BookingPet>.InvalidTransition("Thú cưng không ở trạng thái NOT_ARRIVED để thực hiện check-in.");
        if (booking.HoaDonDonDatLichItems.Any(invoice => invoice.TrangThai != InvoiceState.REVOKED))
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực, không thể check-in.");

        var (vaccinationRequired, frozenSize) = ReadBookingPolicy(pet);
        if (request.MaHoSoTiemChung is { } vaccinationId && !await dbContext.HoSoTiemChungSet
                .AnyAsync(record => record.Id == vaccinationId && record.ThuCungId == pet.ThuCungId, cancellationToken))
            return new CareResult<BookingPet>(404, Message: "Không tìm thấy hồ sơ tiêm chủng của thú cưng.", ErrorCode: "NOT_FOUND");

        if (vaccinationRequired && !request.MaHoSoTiemChung.HasValue && request.XacNhanGiayHopLe != true)
            return new CareResult<BookingPet>(400, Message: "Dịch vụ yêu cầu xác nhận hồ sơ hoặc giấy tiêm chủng hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors:
                [
                    new ApiErrorItem
                    {
                        Field = "xacNhanGiayHopLe",
                        Message = "Cần cung cấp maHoSoTiemChung hoặc xacNhanGiayHopLe = true."
                    }
                ]);

        var assignee = await operation.FindEmployeeWithGrantAsync(request.MaNhanVien, "CARE_UPDATE", cancellationToken);
        if (assignee is null)
            return AssignmentUnavailable("Nhân viên được phân công phải hoạt động và có quyền CARE_UPDATE.");

        var hasCurrentShift = await operation.HasCurrentShiftAsync(assignee.Id, now, cancellationToken);
        if (!hasCurrentShift && actor.VaiTro != Role.ADMIN)
            return AssignmentUnavailable("Nhân viên được phân công không có ca làm việc được duyệt tại thời điểm check-in.");

        var bands = await dbContext.HeSoCanNangSet.AsNoTracking().OrderBy(band => band.CanNangTu).ToListAsync(cancellationToken);
        var actualBand = FindBand(bands, request.CanNangThucTe);
        if (actualBand is null)
            return new CareResult<BookingPet>(400, Message: "Không xác định được khoảng size phù hợp với cân nặng thực tế.",
                ErrorCode: "VALIDATION_FAILED", Errors:
                [new ApiErrorItem { Field = "canNangThucTe", Message = "Cân nặng không khớp bảng size." }]);
        if (preflight)
            return new CareResult<BookingPet>(200);

        var lines = pet.DichVuTrongDonChiTietDonItems.Where(line => line.TrangThai != OrderServiceState.HUY).ToList();
        var committedSize = frozenSize ?? pet.SizeChot ?? lines.Select(line => line.SizeChot).FirstOrDefault(size => size.HasValue)
            ?? FindBand(bands, pet.CanNangKhaiBao)?.MaSize;
        var reduce = request.QuyetDinhGia == PriceDecision.THU_GON;
        if (reduce && committedSize is null)
            return CareResult<BookingPet>.InvalidTransition("Không xác định được size đã cam kết để thu gọn dịch vụ.");

        var finalSize = reduce ? committedSize!.Value : actualBand.MaSize;
        decimal priceDelta = 0m;
        foreach (var line in lines)
        {
            var previousSize = line.SizeChot ?? committedSize;
            var previousPrice = line.GiaChot ?? (previousSize is { } size ? ReadPrice(line, size) : null);
            var lineSize = reduce ? line.SizeChot ?? finalSize : finalSize;
            var finalPrice = reduce ? previousPrice : await ResolveActualPriceAsync(line, actualBand, cancellationToken);
            if (finalPrice is null)
                return CareResult<BookingPet>.InvalidTransition("Không xác định được giá cho dịch vụ trong đơn.");

            line.SizeChot = lineSize;
            line.GiaChot = finalPrice;
            line.UpdatedAt = now;
            if (!reduce)
                priceDelta += finalPrice.Value - (previousPrice ?? 0m);
        }

        if (pet.LoaiPhongId.HasValue)
        {
            var expectedNights = Math.Max(1,
                CareOperation.HcmDate(booking.KetThucDuKien).DayNumber - CareOperation.HcmDate(booking.BatDauDuKien).DayNumber);
            var committedPrice = pet.GiaPhongChot ?? pet.GiaPhongCamKet;
            var finalRoomPrice = committedPrice ?? pet.LoaiPhong?.GiaMoiDem;
            if (finalRoomPrice is null)
                return CareResult<BookingPet>.InvalidTransition("Không xác định được giá phòng cho thú cưng lưu trú.");

            var finalNights = pet.SoDemChot ?? expectedNights;
            var previousRoomTotal = committedPrice.HasValue ? committedPrice.Value * finalNights : 0m;
            pet.GiaPhongChot = finalRoomPrice.Value;
            pet.SoDemChot = finalNights;
            priceDelta += (finalRoomPrice.Value * finalNights) - previousRoomTotal;
        }

        pet.CanNangChot = request.CanNangThucTe;
        pet.SizeThucTe = actualBand.MaSize;
        pet.SizeChot = finalSize;
        pet.ThoaThuanGia = reduce ? request.NoiDungThuGon!.Trim() : PriceDecision.SIZE_THUC_TE.ToString();
        pet.TinhTrangLong = JsonSerializer.Serialize(furConditions);
        pet.GhiChuSucKhoe = request.GhiChuSucKhoe.Trim();
        pet.NhanVienId = assignee.Id;
        pet.HoSoTiemChungId = request.MaHoSoTiemChung ?? pet.HoSoTiemChungId;
        pet.CheckInAt = now;
        pet.CheckInBoiId = actor.Id;
        pet.TrangThaiChamSoc = PetCareState.WAITING;
        pet.UpdatedAt = now;
        if (request.MaHoSoTiemChung.HasValue || request.XacNhanGiayHopLe == true)
        {
            pet.XacMinhTiemAt = now;
            pet.XacMinhTiemBoiId = actor.Id;
        }

        pet.ThuCung.CanNang = request.CanNangThucTe;
        pet.ThuCung.UpdatedAt = now;
        booking.TongTamTinh = Math.Max(0m, booking.TongTamTinh + priceDelta);
        booking.TrangThai = BookingState.IN_PROGRESS;
        booking.UpdatedAt = now;

        dbContext.NhatKyChamSocSet.Add(new NhatKyChamSoc
        {
            ChiTietDonId = pet.Id,
            NhanVienId = actor.Id,
            LoaiNhatKy = "CHECK_IN",
            GhiNhanAt = now,
            ObjectPath = objectPath,
            GhiChu = pet.GhiChuSucKhoe,
            CanNangGhiNhan = request.CanNangThucTe,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CareResult<BookingPet>(200, CareProjection.Pet(pet, actor, assignee.TenNhanVien),
            AssigneeId: assignee.Id, OverrideReason: hasCurrentShift ? null : "ADMIN_OVERRIDE_SHIFT");
    }

    private static CheckinResult ToCheckinResult(CareResult<BookingPet> result) =>
        new(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);

    private static CareResult<BookingPet> AssignmentUnavailable(string message) =>
        new(409, Message: message, ErrorCode: "ASSIGNMENT_UNAVAILABLE");

    private static HeSoCanNang? FindBand(List<HeSoCanNang> bands, decimal weight) =>
        bands.Find(band => weight >= band.CanNangTu && (band.CanNangDen is null || weight < band.CanNangDen));

    private async Task<CareResult<BookingPet>?> ValidateFurConditionsAsync(
        List<string> normalizedFurConditions,
        CancellationToken cancellationToken)
    {
        if (normalizedFurConditions.Count is < 1 or > 20)
        {
            return new CareResult<BookingPet>(
                StatusCodes.Status400BadRequest,
                Message: "Tình trạng lông phải có từ 1 đến 20 mã hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: [new ApiErrorItem { Field = "tinhTrangLong", Message = "Phải chọn từ 1 đến 20 tình trạng lông." }]);
        }

        var furConfig = await dbContext.CauHinhHeThongSet
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Khoa == "FUR_CONDITIONS", cancellationToken);

        var allowedCodes = new HashSet<string>(StringComparer.Ordinal);
        if (furConfig is not null && !string.IsNullOrWhiteSpace(furConfig.GiaTri))
        {
            var items = JsonSerializer.Deserialize<List<FurConditionItem>>(furConfig.GiaTri, CareOperation.JsonOptions) ?? [];
            foreach (var item in items.Where(i => i.DangHoatDong && !string.IsNullOrWhiteSpace(i.Ma)))
            {
                allowedCodes.Add(item.Ma);
            }
        }

        var invalidCodes = normalizedFurConditions.Where(c => !allowedCodes.Contains(c)).ToList();
        if (invalidCodes.Count > 0)
        {
            return new CareResult<BookingPet>(
                StatusCodes.Status400BadRequest,
                Message: "Danh sách tình trạng lông chứa mã không hợp lệ hoặc đã ngừng hoạt động.",
                ErrorCode: "VALIDATION_FAILED",
                Errors:
                [
                    new ApiErrorItem
                    {
                        Field = "tinhTrangLong",
                        Message = $"Mã tình trạng lông không hợp lệ: {string.Join(", ", invalidCodes)}."
                    }
                ]);
        }

        return null;
    }

    private static List<ApiErrorItem> ValidateRequest(int chiTietDonId, Checkin request, CheckinPhotoInput photo)
    {
        var errors = new List<ApiErrorItem>();

        if (chiTietDonId <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        }

        if (request.RequestId == Guid.Empty)
        {
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        }

        if (request.CanNangThucTe is <= 0 or > 999.99m || decimal.Round(request.CanNangThucTe, 2) != request.CanNangThucTe)
        {
            errors.Add(new ApiErrorItem { Field = "canNangThucTe", Message = "Cân nặng thực tế phải > 0, <= 999.99 và tối đa 2 chữ số thập phân." });
        }

        if (request.TinhTrangLong is null || request.TinhTrangLong.Count < 1 || request.TinhTrangLong.Count > 20 ||
            request.TinhTrangLong.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new ApiErrorItem { Field = "tinhTrangLong", Message = "tinhTrangLong phải có từ 1 đến 20 mã không rỗng." });
        }

        if (request.MaNhanVien <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "maNhanVien", Message = "maNhanVien phải lớn hơn 0." });
        }

        if (string.IsNullOrWhiteSpace(request.GhiChuSucKhoe) || request.GhiChuSucKhoe.Trim().Length > 2000)
        {
            errors.Add(new ApiErrorItem { Field = "ghiChuSucKhoe", Message = "ghiChuSucKhoe bắt buộc và tối đa 2000 ký tự." });
        }

        if (!request.QuyetDinhGia.HasValue || !Enum.IsDefined(request.QuyetDinhGia.Value))
        {
            errors.Add(new ApiErrorItem { Field = "quyetDinhGia", Message = "quyetDinhGia bắt buộc." });
        }

        if (!request.XacNhanKhach)
        {
            errors.Add(new ApiErrorItem { Field = "xacNhanKhach", Message = "Khách hàng phải xác nhận đồng thuận trước khi check-in." });
        }

        if (request.QuyetDinhGia == PriceDecision.THU_GON &&
            (string.IsNullOrWhiteSpace(request.NoiDungThuGon) || request.NoiDungThuGon.Trim().Length > 1000))
        {
            errors.Add(new ApiErrorItem { Field = "noiDungThuGon", Message = "noiDungThuGon bắt buộc (1..1000 ký tự) khi chọn THU_GON." });
        }

        if (photo.Length <= 0 || photo.Content.Length == 0)
        {
            errors.Add(new ApiErrorItem { Field = "anhTiepNhan", Message = "Ảnh tiếp nhận bắt buộc." });
        }

        return errors;
    }

    private static (bool VaccinationRequired, SizeBand? CommittedSize) ReadBookingPolicy(ChiTietDon pet)
    {
        var requiredByCatalog = pet.DichVuTrongDonChiTietDonItems.Any(line =>
            line.TrangThai != OrderServiceState.HUY &&
            (line.DichVu?.YeuCauTiemChung == true ||
             line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Any(part => part.DichVu?.YeuCauTiemChung == true)));
        if (string.IsNullOrWhiteSpace(pet.DonDatLich.ChinhSachSnapshot))
            return (requiredByCatalog, null);

        try
        {
            using var document = JsonDocument.Parse(pet.DonDatLich.ChinhSachSnapshot);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (requiredByCatalog, null);

            var legacyRequired = requiredByCatalog ||
                (root.TryGetProperty("yeuCauTiemChung", out var legacy) && legacy.ValueKind == JsonValueKind.True);

            if (root.TryGetProperty("thuCung", out var pets) && pets.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in pets.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("maChiTietDon", out var id) ||
                        id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var persistedId) || persistedId != pet.Id)
                        continue;

                    var required = entry.TryGetProperty("yeuCauTiemChung", out var vaccine) &&
                        vaccine.ValueKind is JsonValueKind.True or JsonValueKind.False ? vaccine.GetBoolean() : legacyRequired;
                    SizeBand? size = entry.TryGetProperty("sizeCamKet", out var frozen) &&
                        frozen.ValueKind == JsonValueKind.String && Enum.TryParse<SizeBand>(frozen.GetString(), out var parsed) &&
                        Enum.IsDefined(parsed) ? parsed : null;
                    return (required, size);
                }
            }
            return (legacyRequired, null);
        }
        catch (JsonException)
        {
            return (requiredByCatalog, null);
        }
    }

    private async Task<decimal?> ResolveActualPriceAsync(DichVuTrongDon line, HeSoCanNang band, CancellationToken cancellationToken)
    {
        if (ReadPrice(line, band.MaSize) is { } snapshot)
            return snapshot;
        if (line.DichVuId is { } serviceId)
            return await dbContext.BangGiaSizeSet.Where(price => price.DichVuId == serviceId && price.SizeId == band.Id)
                .Select(price => (decimal?)price.DonGia).FirstOrDefaultAsync(cancellationToken);
        if (line.ComboId is { } comboId)
            return await dbContext.BangGiaSizeSet.Where(price => price.ComboId == comboId && price.SizeId == band.Id)
                .Select(price => (decimal?)price.DonGia).FirstOrDefaultAsync(cancellationToken);
        return line.GiaChot;
    }

    private static decimal? ReadPrice(DichVuTrongDon line, SizeBand size)
    {
        if (string.IsNullOrWhiteSpace(line.BangGiaSizeSnapshot))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line.BangGiaSizeSnapshot);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var price in document.RootElement.EnumerateObject())
                {
                    if (price.Name.Equals(size.ToString(), StringComparison.OrdinalIgnoreCase) &&
                        price.Value.ValueKind == JsonValueKind.Number && price.Value.TryGetDecimal(out var amount))
                        return amount;
                }
            }
        }
        catch (JsonException)
        {
            // Older bookings may not have a readable size/price snapshot.
        }
        return null;
    }
}
