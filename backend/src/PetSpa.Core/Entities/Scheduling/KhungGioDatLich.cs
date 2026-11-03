using PetSpa.Core.Enums;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Scheduling;

namespace PetSpa.Core.Entities.Scheduling;

public class KhungGioDatLich
{
    public int Id { get; set; }
    public DateOnly Ngay { get; set; }
    public bool MoNhanKhach { get; set; }
    public int TongSlot { get; set; }
    public int SlotDaDat { get; set; }
    public int TranPhut { get; set; }
    public int PhutDaGiu { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int CaLamViecId { get; set; }

    public CaLamViec CaLamViec { get; set; } = null!;
    public ICollection<DonDatLich> DonDatLichKhungGioItems { get; set; } = new List<DonDatLich>();
    public ICollection<DichVuTrongDon> DichVuTrongDonKhungGioItems { get; set; } = new List<DichVuTrongDon>();
}
