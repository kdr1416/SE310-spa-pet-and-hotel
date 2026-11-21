using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Core.Services;

namespace PetSpa.Api.Services;

public class BookingService : IBookingService
{
    private readonly PetSpaDbContext _context;

    public BookingService(PetSpaDbContext context)
    {
        _context = context;
    }

    public async Task<BookingServiceResult> CreateBookingAsync(
        BookingCreate command,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (command == null)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Dữ liệu đặt lịch không hợp lệ.", "VALIDATION_FAILED"),
                StatusCode: StatusCodes.Status400BadRequest
            );
        }

        if (command.RequestId == Guid.Empty)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("RequestId là bắt buộc.", "VALIDATION_FAILED"),
                StatusCode: StatusCodes.Status400BadRequest
            );
        }

        if (command.LienHe == null ||
            string.IsNullOrWhiteSpace(command.LienHe.HoTen) ||
            string.IsNullOrWhiteSpace(command.LienHe.SoDienThoai) ||
            string.IsNullOrWhiteSpace(command.LienHe.Email))
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Thông tin liên hệ (họ tên, SĐT, email) là bắt buộc.", "VALIDATION_FAILED"),
                StatusCode: StatusCodes.Status400BadRequest
            );
        }

        if (command.ThuCung == null || command.ThuCung.Count == 0 || command.ThuCung.Count > 20)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Đơn đặt lịch phải có từ 1 đến 20 thú cưng.", "VALIDATION_FAILED"),
                StatusCode: StatusCodes.Status400BadRequest
            );
        }

        var fingerprint = ComputeFingerprint(rawPayload);

        // 1. Kiểm tra Idempotency theo RequestId
        var existing = await _context.DonDatLichSet
            .Include(d => d.KhachHang)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.ThuCung)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.DichVuTrongDonChiTietDonItems)
                    .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .FirstOrDefaultAsync(d => d.RequestId == command.RequestId, cancellationToken);

        if (existing != null)
        {
            if (existing.Fingerprint == fingerprint)
            {
                return new BookingServiceResult(
                    ApiResponse<BookingResponse>.Ok(MapToResponse(existing), "Đơn đặt lịch đã tồn tại (idempotent replay)."),
                    IsReplay: true,
                    StatusCode: StatusCodes.Status200OK
                );
            }

            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("RequestId đã được sử dụng với nội dung khác.", "IDEMPOTENCY_CONFLICT"),
                IsReplay: false,
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        // 2. Kiểm tra Khung giờ và Sức chứa (Capacity Guard)
        var khungGio = await _context.KhungGioDatLichSet
            .Include(k => k.CaLamViec)
            .FirstOrDefaultAsync(k => k.Id == command.MaKhungGio, cancellationToken);

        if (khungGio == null)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Khung giờ đặt lịch không tồn tại.", "VALIDATION_FAILED"),
                StatusCode: StatusCodes.Status400BadRequest
            );
        }

        if (!khungGio.MoNhanKhach)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Khung giờ hiện đang đóng tiếp nhận khách.", "CAPACITY_EXCEEDED"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var laNgayNghi = await _context.NgayNghiSet.AnyAsync(n => n.Ngay == khungGio.Ngay, cancellationToken);
        if (laNgayNghi)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Ngày đã chọn rơi vào ngày nghỉ của cơ sở.", "CAPACITY_EXCEEDED"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var ca = khungGio.CaLamViec;
        var shiftDurationMinutes = (int)(ca.GioKetThuc - ca.GioBatDau).TotalMinutes;
        if (shiftDurationMinutes <= 0) shiftDurationMinutes = 240;

        var approvedCareStaffCount = await _context.DangKyCaSet
            .Where(r => r.Ngay == khungGio.Ngay && r.CaLamViecId == ca.Id && r.TrangThai == ShiftRegistrationState.APPROVED)
            .Where(r => r.NhanVien.VaiTro == Role.ADMIN || r.NhanVien.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == "CARE_UPDATE"))
            .Select(r => r.NhanVienId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (approvedCareStaffCount == 0)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Ca làm việc chưa có nhân sự chăm sóc được phê duyệt.", "CAPACITY_EXCEEDED"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var soPetSpa = command.ThuCung.Count(p => p.DichVu.Count > 0);
        var slotConLai = Math.Max(0, ca.SucChuaPet - khungGio.SlotDaDat);
        if (soPetSpa > slotConLai)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Số lượng thú cưng vượt quá sức chứa còn lại của ca.", "CAPACITY_EXCEEDED"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var serviceIds = command.ThuCung.SelectMany(p => p.DichVu)
            .Where(d => d.MaDichVu.HasValue)
            .Select(d => d.MaDichVu!.Value)
            .Distinct()
            .ToList();

        var serviceDurations = await _context.DichVuSet
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.ThoiLuongPhut, cancellationToken);

        var tongPhutYeuCau = command.ThuCung
            .SelectMany(p => p.DichVu)
            .Where(d => d.MaDichVu.HasValue && serviceDurations.ContainsKey(d.MaDichVu.Value))
            .Sum(d => serviceDurations[d.MaDichVu!.Value]);

        var tranPhutHieuLuc = Math.Min(ca.TranPhut, approvedCareStaffCount * shiftDurationMinutes);
        var phutConLai = Math.Max(0, tranPhutHieuLuc - khungGio.PhutDaGiu);
        if (tongPhutYeuCau > phutConLai)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Tổng thời gian dịch vụ vượt quá số phút khả dụng của ca.", "CAPACITY_EXCEEDED"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        // 3. Khách hàng: Tìm theo SĐT hoặc tạo mới (D10 / Q05)
        var customer = await _context.KhachHangSet
            .FirstOrDefaultAsync(k => k.SoDienThoai == command.LienHe.SoDienThoai, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (customer == null)
        {
            customer = new KhachHang
            {
                HoTen = command.LienHe.HoTen,
                SoDienThoai = command.LienHe.SoDienThoai,
                Email = command.LienHe.Email,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.KhachHangSet.Add(customer);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // 4. Lấy hệ số bảng giá size và danh mục
        var heSoCanNangList = await _context.HeSoCanNangSet
            .Include(h => h.BangGiaSizeKhoangSizeItems)
            .ToListAsync(cancellationToken);

        var allServices = await _context.DichVuSet
            .Include(s => s.BangGiaSizeDichVuItems)
                .ThenInclude(b => b.KhoangSize)
            .ToListAsync(cancellationToken);

        var allCombos = await _context.ComboSet
            .Include(c => c.ChiTietComboComboItems)
                .ThenInclude(ct => ct.DichVu)
            .Include(c => c.BangGiaSizeComboItems)
                .ThenInclude(b => b.KhoangSize)
            .ToListAsync(cancellationToken);

        var allRooms = await _context.LoaiPhongSet.ToListAsync(cancellationToken);

        // 5. Khởi tạo Đơn Đặt Lịch
        var startDateTime = khungGio.Ngay.ToDateTime(ca.GioBatDau);
        var endDateTime = command.NgayTra.ToDateTime(ca.GioKetThuc);
        var batDauDuKien = new DateTimeOffset(startDateTime, TimeSpan.FromHours(7));
        var ketThucDuKien = new DateTimeOffset(endDateTime, TimeSpan.FromHours(7));
        var mocCuoiCa = batDauDuKien.Date.Add(ca.GioKetThuc.ToTimeSpan()).AddMinutes(30);

        var trackingCode = await GenerateUniqueTrackingCodeAsync(cancellationToken);

        var donDatLich = new DonDatLich
        {
            RequestId = command.RequestId,
            Fingerprint = fingerprint,
            TrackingCode = trackingCode,
            TrangThai = BookingState.PENDING,
            TenNguoiDat = command.LienHe.HoTen,
            SoDienThoaiSnapshot = command.LienHe.SoDienThoai,
            EmailSnapshot = command.LienHe.Email,
            BatDauDuKien = batDauDuKien,
            KetThucDuKien = ketThucDuKien,
            MocCuoiCa = mocCuoiCa,
            TongTamTinh = 0m,
            EmailDelivery = "SENT",
            CreatedAt = now,
            UpdatedAt = now,
            KhachHangId = customer.Id,
            KhungGioId = khungGio.Id
        };

        decimal tongTamTinh = 0m;

        foreach (var petReq in command.ThuCung)
        {
            ThuCung? petEntity = null;
            if (petReq.MaThuCung.HasValue)
            {
                petEntity = await _context.ThuCungSet.FindAsync([petReq.MaThuCung.Value], cancellationToken);
            }

            if (petEntity == null)
            {
                petEntity = new ThuCung
                {
                    TenThuCung = string.IsNullOrWhiteSpace(petReq.TenThuCung) ? "Thú cưng" : petReq.TenThuCung,
                    LoaiPet = string.Equals(petReq.LoaiPet, "MEO", StringComparison.OrdinalIgnoreCase) ? PetType.MEO : PetType.CHO,
                    CanNang = petReq.CanNangKhaiBao,
                    Giong = petReq.Giong,
                    LoaiLong = petReq.LoaiLong,
                    GhiChuDiUng = petReq.GhiChuDiUng,
                    KhachHangId = customer.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.ThuCungSet.Add(petEntity);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Tính size theo cân nặng khai báo
            var heSo = heSoCanNangList.FirstOrDefault(h =>
                petReq.CanNangKhaiBao >= h.CanNangTu &&
                (!h.CanNangDen.HasValue || petReq.CanNangKhaiBao <= h.CanNangDen.Value)
            );

            var sizeBand = heSo?.MaSize ?? (petReq.CanNangKhaiBao switch
            {
                < 5m => SizeBand.S,
                < 15m => SizeBand.M,
                < 25m => SizeBand.L,
                _ => SizeBand.XL
            });

            var chiTietDon = new ChiTietDon
            {
                TenPetSnapshot = petEntity.TenThuCung,
                TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
                CanNangKhaiBao = petReq.CanNangKhaiBao,
                SizeChot = sizeBand,
                SizeThucTe = sizeBand,
                CreatedAt = now,
                UpdatedAt = now,
                ThuCungId = petEntity.Id,
                ThuCung = petEntity,
                DonDatLich = donDatLich
            };

            // Hotel nếu có
            if (petReq.MaLoaiPhong.HasValue)
            {
                var room = allRooms.FirstOrDefault(r => r.Id == petReq.MaLoaiPhong.Value);
                if (room != null)
                {
                    int soDem = Math.Max(1, command.NgayTra.DayNumber - khungGio.Ngay.DayNumber);
                    decimal giaPhong = room.GiaMoiDem * soDem;
                    chiTietDon.LoaiPhongId = room.Id;
                    chiTietDon.GiaPhongCamKet = giaPhong;
                    chiTietDon.SoDemChot = soDem;
                    tongTamTinh += giaPhong;
                }
            }

            // Dịch vụ Spa
            foreach (var svReq in petReq.DichVu)
            {
                if (svReq.MaDichVu.HasValue)
                {
                    var sv = allServices.FirstOrDefault(s => s.Id == svReq.MaDichVu.Value);
                    if (sv != null)
                    {
                        var bg = sv.BangGiaSizeDichVuItems.FirstOrDefault(b =>
                            b.KhoangSize?.MaSize == sizeBand || (heSo != null && b.SizeId == heSo.Id));
                        decimal donGia = bg?.DonGia ?? 0m;

                        var dictSnapshot = sv.BangGiaSizeDichVuItems
                            .Where(b => b.KhoangSize != null)
                            .ToDictionary(b => b.KhoangSize.MaSize.ToString(), b => b.DonGia);

                        var dichVuTrongDon = new DichVuTrongDon
                        {
                            RequestId = Guid.NewGuid(),
                            DichVuId = sv.Id,
                            KhungGioId = svReq.MaKhungGio > 0 ? svReq.MaKhungGio : khungGio.Id,
                            TenSnapshot = sv.TenDichVu,
                            ThoiLuongSnapshot = sv.ThoiLuongPhut,
                            TrangThai = OrderServiceState.CHO_THUC_HIEN,
                            SizeChot = sizeBand,
                            GiaChot = donGia,
                            BangGiaSizeSnapshot = JsonSerializer.Serialize(dictSnapshot),
                            CreatedAt = now,
                            UpdatedAt = now,
                            ChiTietDon = chiTietDon
                        };
                        chiTietDon.DichVuTrongDonChiTietDonItems.Add(dichVuTrongDon);
                        tongTamTinh += donGia;
                    }
                }
                else if (svReq.MaCombo.HasValue)
                {
                    var cb = allCombos.FirstOrDefault(c => c.Id == svReq.MaCombo.Value);
                    if (cb != null)
                    {
                        var bg = cb.BangGiaSizeComboItems.FirstOrDefault(b =>
                            b.KhoangSize?.MaSize == sizeBand || (heSo != null && b.SizeId == heSo.Id));
                        decimal donGia = bg?.DonGia ?? 0m;

                        var dictSnapshot = cb.BangGiaSizeComboItems
                            .Where(b => b.KhoangSize != null)
                            .ToDictionary(b => b.KhoangSize.MaSize.ToString(), b => b.DonGia);

                        var dichVuTrongDon = new DichVuTrongDon
                        {
                            RequestId = Guid.NewGuid(),
                            ComboId = cb.Id,
                            KhungGioId = svReq.MaKhungGio > 0 ? svReq.MaKhungGio : khungGio.Id,
                            TenSnapshot = cb.TenCombo,
                            ThoiLuongSnapshot = cb.ChiTietComboComboItems.Sum(ct => ct.DichVu.ThoiLuongPhut),
                            TrangThai = OrderServiceState.CHO_THUC_HIEN,
                            SizeChot = sizeBand,
                            GiaChot = donGia,
                            BangGiaSizeSnapshot = JsonSerializer.Serialize(dictSnapshot),
                            CreatedAt = now,
                            UpdatedAt = now,
                            ChiTietDon = chiTietDon
                        };

                        foreach (var ct in cb.ChiTietComboComboItems)
                        {
                            dichVuTrongDon.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Add(new ThanhPhanDichVuTrongDon
                            {
                                DichVuId = ct.DichVuId,
                                TenSnapshot = ct.DichVu.TenDichVu,
                                ThoiLuongSnapshot = ct.DichVu.ThoiLuongPhut,
                                TrangThai = OrderServiceState.CHO_THUC_HIEN,
                                CreatedAt = now,
                                UpdatedAt = now,
                                DichVuTrongDon = dichVuTrongDon
                            });
                        }

                        chiTietDon.DichVuTrongDonChiTietDonItems.Add(dichVuTrongDon);
                        tongTamTinh += donGia;
                    }
                }
            }

            donDatLich.ChiTietDonDonDatLichItems.Add(chiTietDon);
        }

        donDatLich.TongTamTinh = tongTamTinh;

        // 6. Cập nhật công suất ca (Atomic Transaction)
        khungGio.SlotDaDat += soPetSpa;
        khungGio.PhutDaGiu += tongPhutYeuCau;

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        _context.DonDatLichSet.Add(donDatLich);
        await _context.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new BookingServiceResult(
            ApiResponse<BookingResponse>.Ok(MapToResponse(donDatLich), "Đặt lịch thành công."),
            IsReplay: false,
            StatusCode: StatusCodes.Status201Created
        );
    }

    public async Task<ApiResponse<PageBooking>> GetBookingsAsync(
        BookingQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.DonDatLichSet
            .Include(d => d.KhachHang)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.ThuCung)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.DichVuTrongDonChiTietDonItems)
                    .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.TrangThai) &&
            Enum.TryParse<BookingState>(query.TrangThai, true, out var state))
        {
            q = q.Where(d => d.TrangThai == state);
        }

        if (query.TuNgay.HasValue)
        {
            var tu = new DateTimeOffset(query.TuNgay.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
            q = q.Where(d => d.BatDauDuKien >= tu);
        }

        if (query.DenNgay.HasValue)
        {
            var den = new DateTimeOffset(query.DenNgay.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(7));
            q = q.Where(d => d.BatDauDuKien <= den);
        }

        var total = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Trang);
        var pageSize = Math.Clamp(query.KichThuocTrang, 1, 100);

        var items = await q
            .OrderByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = new PageBooking
        {
            Items = items.Select(MapToResponse).ToList(),
            TongSo = total,
            Trang = page,
            KichThuocTrang = pageSize,
            TongSoTrang = (int)Math.Ceiling((double)total / pageSize)
        };

        return ApiResponse<PageBooking>.Ok(result);
    }

    public async Task<ApiResponse<BookingResponse>> GetBookingByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var booking = await _context.DonDatLichSet
            .Include(d => d.KhachHang)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.ThuCung)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.DichVuTrongDonChiTietDonItems)
                    .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (booking == null)
        {
            return ApiResponse<BookingResponse>.Fail("Không tìm thấy đơn đặt lịch.", "BOOKING_NOT_FOUND");
        }

        return ApiResponse<BookingResponse>.Ok(MapToResponse(booking));
    }

    public async Task<BookingServiceResult> ConfirmBookingAsync(
        int id,
        ConfirmBookingCommand command,
        int? actorId = null,
        CancellationToken cancellationToken = default)
    {
        var booking = await _context.DonDatLichSet
            .Include(d => d.KhachHang)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.ThuCung)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.DichVuTrongDonChiTietDonItems)
                    .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (booking == null)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Không tìm thấy đơn đặt lịch.", "BOOKING_NOT_FOUND"),
                StatusCode: StatusCodes.Status404NotFound
            );
        }

        if (booking.TrangThai != BookingState.PENDING)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Chỉ có thể xác nhận đơn đặt lịch đang ở trạng thái PENDING.", "INVALID_TRANSITION"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var now = DateTimeOffset.UtcNow;
        booking.TrangThai = BookingState.CONFIRMED;
        booking.DuyetAt = now;
        booking.DuyetBoiId = actorId;
        booking.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return new BookingServiceResult(
            ApiResponse<BookingResponse>.Ok(MapToResponse(booking), "Xác nhận đơn đặt lịch thành công."),
            StatusCode: StatusCodes.Status200OK
        );
    }

    public async Task<BookingServiceResult> CancelBookingAsync(
        int id,
        CancelBookingCommand command,
        int? actorId = null,
        CancellationToken cancellationToken = default)
    {
        var booking = await _context.DonDatLichSet
            .Include(d => d.KhungGio)
            .Include(d => d.KhachHang)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.ThuCung)
            .Include(d => d.ChiTietDonDonDatLichItems)
                .ThenInclude(c => c.DichVuTrongDonChiTietDonItems)
                    .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (booking == null)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Không tìm thấy đơn đặt lịch.", "BOOKING_NOT_FOUND"),
                StatusCode: StatusCodes.Status404NotFound
            );
        }

        if (booking.TrangThai == BookingState.CANCELLED)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Đơn đặt lịch đã bị hủy trước đó.", "INVALID_TRANSITION"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        if (booking.TrangThai != BookingState.PENDING && booking.TrangThai != BookingState.CONFIRMED)
        {
            return new BookingServiceResult(
                ApiResponse<BookingResponse>.Fail("Không thể hủy đơn đặt lịch ở trạng thái hiện tại.", "INVALID_TRANSITION"),
                StatusCode: StatusCodes.Status409Conflict
            );
        }

        var now = DateTimeOffset.UtcNow;
        booking.TrangThai = BookingState.CANCELLED;
        booking.HuyAt = now;
        booking.HuyBoiId = actorId;
        booking.LoaiHuy = command.LoaiHuy;
        booking.LyDoHuy = command.LyDo;
        booking.UpdatedAt = now;

        // Cập nhật trạng thái từng thú cưng nếu chưa hoàn tất
        foreach (var pet in booking.ChiTietDonDonDatLichItems)
        {
            if (pet.TrangThaiChamSoc != PetCareState.DONE)
            {
                pet.TrangThaiChamSoc = PetCareState.CANCELLED;
                pet.HuyAt = now;
                pet.LyDoHuy = command.LyDo;
            }
        }

        // Hoàn trả công suất đã giữ (D04 / Q01)
        if (booking.KhungGio != null)
        {
            var petSpaCount = booking.ChiTietDonDonDatLichItems.Count(p => p.DichVuTrongDonChiTietDonItems.Count > 0);
            var totalMinutes = booking.ChiTietDonDonDatLichItems
                .SelectMany(p => p.DichVuTrongDonChiTietDonItems)
                .Sum(s => s.ThoiLuongSnapshot);

            booking.KhungGio.SlotDaDat = Math.Max(0, booking.KhungGio.SlotDaDat - petSpaCount);
            booking.KhungGio.PhutDaGiu = Math.Max(0, booking.KhungGio.PhutDaGiu - totalMinutes);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new BookingServiceResult(
            ApiResponse<BookingResponse>.Ok(MapToResponse(booking), "Hủy đơn đặt lịch thành công."),
            StatusCode: StatusCodes.Status200OK
        );
    }

    private static string ComputeFingerprint(string payload)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<string> GenerateUniqueTrackingCodeAsync(CancellationToken cancellationToken)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = Random.Shared;
        while (true)
        {
            var code = "TRK" + new string(Enumerable.Range(0, 5).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            var exists = await _context.DonDatLichSet.AnyAsync(d => d.TrackingCode == code, cancellationToken);
            if (!exists) return code;
        }
    }

    private static BookingResponse MapToResponse(DonDatLich d)
    {
        return new BookingResponse
        {
            MaDon = d.Id,
            TrackingCode = d.TrackingCode,
            TrangThai = d.TrangThai.ToString(),
            BatDauDuKien = d.BatDauDuKien,
            KetThucDuKien = d.KetThucDuKien,
            LienHe = new Contact
            {
                HoTen = d.TenNguoiDat,
                SoDienThoai = d.SoDienThoaiSnapshot,
                Email = d.EmailSnapshot
            },
            TongTamTinh = d.TongTamTinh,
            EmailDelivery = d.EmailDelivery ?? "SENT",
            ThuCung = d.ChiTietDonDonDatLichItems.Select(c => new BookingPet
            {
                MaChiTietDon = c.Id,
                MaThuCung = c.ThuCungId,
                TenThuCung = c.TenPetSnapshot,
                TrangThai = c.TrangThaiChamSoc.ToString(),
                CanNangChot = c.CanNangChot,
                SizeThucTe = c.SizeThucTe?.ToString(),
                SizeChot = c.SizeChot?.ToString(),
                MaLoaiPhong = c.LoaiPhongId,
                NhanSom = c.NhanSom,
                BanGiaoAt = c.BanGiaoAt,
                TraLaiAt = c.TraLaiAt,
                DichVu = c.DichVuTrongDonChiTietDonItems.Select(s => new OrderService
                {
                    MaDichVuTrongDon = s.Id,
                    MaDichVu = s.DichVuId,
                    MaCombo = s.ComboId,
                    MaKhungGio = s.KhungGioId,
                    TenDichVu = s.TenSnapshot,
                    TrangThai = s.TrangThai.ToString(),
                    SizeChot = s.SizeChot?.ToString(),
                    GiaChot = s.GiaChot,
                    ThanhPhan = s.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Select(tp => new Component
                    {
                        MaThanhPhan = tp.Id,
                        MaDichVu = tp.DichVuId,
                        TenDichVu = tp.TenSnapshot,
                        TrangThai = tp.TrangThai.ToString(),
                        HoanTatAt = tp.HoanTatAt
                    }).ToList()
                }).ToList()
            }).ToList()
        };
    }
}
