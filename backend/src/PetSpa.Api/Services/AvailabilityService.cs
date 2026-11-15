using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Availability ;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums ;
using PetSpa.Core.Services ;

namespace PetSpa.Api.Services ;
public class AvailabilityService : IAvailabilityService
{
    private readonly PetSpaDbContext _context;
    
    // Dependency Injection: .NET tự động truyền DbContext vào đây
    public AvailabilityService(PetSpaDbContext context)
    {
        _context = context ;
    }
    public async Task<AvailabilityResponse>
CheckAvailabilityAsync(
    AvailabilityQuery query,
    CancellationToken cancellationToken = default
)
    {
        //1. Lấy thông tin KhungGioDatLich và CaLamViec liên quan
        var khungGio =await _context.KhungGioDatLichSet
        .Include(k=> k.CaLamViec)
        .FirstOrDefaultAsync(k=> k.Id == query.MaKhungGio,cancellationToken);

        if (khungGio == null)
        {
            return new AvailabilityResponse
             {
                MaKhungGio = query.MaKhungGio ,
                CoTheDat = false ,
                LyDo = " Khung giờ đặt lịch không tồn tại"
             } ;
        }
        var ca = khungGio.CaLamViec ;
        var ngay = khungGio.Ngay ;
        // 2. Kiểm tra nếu khung giờ đang đóng nhận khách
        if(!khungGio.MoNhanKhach) 
        {
          return new AvailabilityResponse
          {
            MaKhungGio = query.MaKhungGio ,
            CoTheDat = false ,
            LyDo = "khung giờ hiện đang đóng tiếp nhận khách."
          };
        }
        //3. Kiểm tra ngày nghỉ (ngay_nghi)
        var laNgayNghi = await _context.NgayNghiSet
          .AnyAsync(n => n.Ngay == ngay, cancellationToken) ;
          if (laNgayNghi)
          {
            return new AvailabilityResponse
            {
                MaKhungGio = query.MaKhungGio ,
                CoTheDat = false ,
                LyDo = "Ngày đã chọn rơi vào ngày nghỉ của cơ sở ."
            } ;
          }
          //4. Tính tổng phút của các nhân viên APPROVED có quyền chăm sóc (CARE_UPDATE hoặc ADMIN)
          var shiftDurationMinutes = (int)(ca.GioKetThuc - ca.GioBatDau).TotalMinutes ;
          if (shiftDurationMinutes <=0) shiftDurationMinutes = 240 ;
          // Mặc định cả 4 tiếng nếu chưa set giờ
          var approvedCareStaffCount = await _context.DangKyCaSet
          .Where(r => r.Ngay == ngay && r.CaLamViecId == ca.Id && r.TrangThai == ShiftRegistrationState.APPROVED)
          .Where(r=> r.NhanVien.VaiTro == Role.ADMIN || r.NhanVien.NhanVienQuyenNhanVienItems.Any(q=>
            q.MaQuyen == "CARE_UPDATE"))
            .Select(r=> r.NhanVienId)
            .Distinct()
            .CountAsync(cancellationToken) ;

            var tongPhutNhanVien = approvedCareStaffCount * shiftDurationMinutes ;
            var tranPhutHieuLuc = Math.Min(ca.TranPhut, tongPhutNhanVien) ;

            // 5. Tính số lượng pet và số phút dịch vụ mà khách yêu cầu trong query này
            var soPetYeuCau = query.ThuCung.Count ;
            // Gom danh sách tất cả mã dịch vụ được chọn của các pet
            var serviceIds = query.ThuCung.SelectMany(p =>p.DichVu)
                                    .Where(d => d.MaDichVu.HasValue)
                                    .Select(d => d.MaDichVu!.Value)
                                    .Distinct()
                                    .ToList() ;
            var serviceDurations = await _context.DichVuSet
              .Where(s=> serviceIds.Contains(s.Id))
              .ToDictionaryAsync(s => s.Id, s => s.ThoiLuongPhut,cancellationToken) ;
            
            var tongPhutYeuCau = query.ThuCung 
              .SelectMany(p => p.DichVu)
              .Where(d => d.MaDichVu.HasValue && serviceDurations.ContainsKey(d.MaDichVu.Value))
              .Sum(d => serviceDurations[d.MaDichVu!.Value]) ;

            // 6. Tính số slot và số phút còn lại của ca        
            var slotConLai = Math.Max(0, ca.SucChuaPet - khungGio.SlotDaDat) ;
            var phutConLai = Math.Max(0, tranPhutHieuLuc - khungGio.PhutDaGiu) ;

            //7. kiểm tra 2 trần đồng thời
            if (approvedCareStaffCount == 0)
            {
                return new AvailabilityResponse{
                    MaKhungGio = query.MaKhungGio,
                    SlotConLai = slotConLai,
                    PhutConLai = 0 ,
                    CoTheDat = false,
                    LyDo = "Ca làm việc chưa có nhân sự chăm sóc được phê duyệt."
                } ;
            }

            if(soPetYeuCau > slotConLai)
            {
                return new AvailabilityResponse
                {
                    MaKhungGio = query.MaKhungGio,
                    SlotConLai = slotConLai ,
                    PhutConLai = phutConLai ,
                    CoTheDat = false ,
                    LyDo = "Số lượng thú cưng vượt quá sức chứa còn lại của ca."

                };
            }
            
            if (tongPhutYeuCau > phutConLai)
            {
                return new AvailabilityResponse
                {
                    MaKhungGio = query.MaKhungGio ,
                    SlotConLai = slotConLai ,
                    PhutConLai = phutConLai,
                    CoTheDat = false ,
                    LyDo = "Tổng thời gian dịch vụ vượt quá số phút khả dụng của ca."

                } ;
            }
            //Đạt tất cả điều kiện : Có thể đặt !
            return new AvailabilityResponse
            {
                MaKhungGio = query.MaKhungGio,
                SlotConLai = slotConLai,
                PhutConLai = phutConLai,
                CoTheDat = true ,
                LyDo =null
            } ;
    }
}