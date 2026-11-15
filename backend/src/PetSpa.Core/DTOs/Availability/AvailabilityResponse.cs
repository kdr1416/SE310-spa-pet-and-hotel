namespace PetSpa.Core.DTOs.Availability;
public class AvailabilityResponse{
    public int MaKhungGio {get; set;}
    public int SlotConLai {get; set;}
    public int PhutConLai {get; set;}
    public List<RoomAvailability> Phong{get; set;} = new() ;
    public bool CoTheDat {get; set;}
    public string? LyDo {get ; set;}
}
public class RoomAvailability
{
    public int MaLoaiPhong {get; set;}
    public DateOnly Ngay {get; set;}
    public int SuatConLai {get; set;}
}