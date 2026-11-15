namespace PetSpa.Core.DTOs.Availability ;
public class AvailabilityQuery
{
    public int MaKhungGio {get;set;}
    public DateOnly NgayTra {get;set;}
    public List<PetDemand> ThuCung {get;set;} = new() ;
}

public class PetDemand
{
    public int? MaLoaiPhong {get;set;}
    public List<ServiceSelection> DichVu {get;set;} =new() ;
}
public class ServiceSelection 
{
    public int? MaDichVu {get;set;}
    public int? MaCombo {get;set;}
    public int MaKhungGio {get;set;}
}