using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Tests.Foundation;

public sealed class SharedDtoContractTests
{
    private const string Id = "9de41cc1-c2b9-41a6-875c-99a29cd5cecb";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, string> ValidRequests => new()
    {
        { "Billing.ReceiptCreate", Command("\"soTien\":100000,\"phuongThuc\":\"CASH\",\"giaoDichAt\":\"2035-01-01T08:00:00+07:00\"") },
        { "Billing.RefundCreate", Command("\"soTien\":100000,\"lyDo\":\"Hoàn cọc\"") },
        { "Billing.RefundConfirm", Command("\"phuongThuc\":\"BANK_TRANSFER\",\"hoanAt\":\"2035-01-01T08:00:00+07:00\",\"thamChieu\":\"TEST-12345678\"") },
        { "Billing.FeeCreate", Command("\"tenPhuPhi\":\"Gỡ rối\",\"soTien\":50000,\"lyDo\":\"Khách đồng ý\"") },
        { "Billing.FeeEdit", Command("\"soTien\":60000,\"lyDo\":\"Điều chỉnh\"") },
        { "Billing.ExtraServiceRequest", Command("\"maDichVu\":1,\"maKhungGio\":2,\"lyDo\":\"Thêm dịch vụ\"") },
        { "Billing.ServiceActionRequest", Command("\"hanhDong\":\"COMPLETE\"") },
        { "Billing.ReasonCommand", Command("\"lyDo\":\"Khách yêu cầu\"") },
        { "Booking.CustomerQuery", "{\"soDienThoai\":\"0901234567\"}" },
        { "Booking.PetEdit", Command("\"tenThuCung\":\"Milo\"") },
        { "Booking.RescheduleRequest", Command("\"maKhungGio\":2,\"ngayTra\":\"2035-01-02\",\"lyDo\":\"Dời lịch\"") },
        { "Booking.TrackingCancel", Command("\"soDienThoai\":\"0901234567\",\"trackingCode\":\"TRK-1111\",\"lyDo\":\"Khách hủy\"") },
        { "Booking.TrackingResend", "{\"soDienThoai\":\"0901234567\",\"email\":\"guest@example.test\"}" },
        { "Booking.AssignmentRequest", Command("\"maNhanVien\":1,\"lyDo\":\"Đổi người phụ trách\"") },
        { "Booking.ShiftQuery", "{\"ngay\":\"2035-01-01\",\"maCa\":1}" },
        { "Booking.PetCancelRequest", Command("\"lyDo\":\"Hủy riêng\",\"soDemDaO\":0,\"maThanhPhanDaLam\":[]") },
        { "Scheduling.ScheduleQuery", "{\"tuNgay\":\"2035-01-01\",\"denNgay\":\"2035-01-02\",\"maNhanVien\":1}" },
        { "Scheduling.ShiftRegisterRequest", Command("\"ngay\":\"2035-01-01\",\"maCa\":1") },
        { "Scheduling.ShiftDecisionRequest", Command("\"quyetDinh\":\"APPROVE\"") },
        { "Scheduling.ShiftWindowWriteRequest", Command("\"tuNgay\":\"2035-01-01\",\"denNgay\":\"2035-01-02\",\"moAt\":\"2034-12-01T00:00:00Z\",\"dongAt\":\"2034-12-31T00:00:00Z\"") },
        { "Scheduling.ShiftTransferRequest", Command("\"ngay\":\"2035-01-01\",\"maCa\":1,\"lyDo\":\"Điều chuyển\"") }
    };

    [Theory, MemberData(nameof(ValidRequests))]
    public void Requests_DeserializeAndValidateWithoutExtraBusinessFields(string name, string json)
    {
        var request = Deserialize(name, json);
        Assert.Empty(Errors(request));
        using var input = JsonDocument.Parse(json);
        using var output = JsonDocument.Parse(JsonSerializer.Serialize(request, request.GetType(), Options));
        foreach (var property in input.RootElement.EnumerateObject())
            Assert.True(output.RootElement.TryGetProperty(property.Name, out _), property.Name);
        Assert.False(output.RootElement.TryGetProperty("actorId", out _));
    }

    [Theory]
    [InlineData("Booking.PetEdit", "tenThuCung")]
    [InlineData("Booking.PetEdit", "canNang")]
    [InlineData("Billing.FeeEdit", "soTien")]
    [InlineData("Billing.RefundConfirm", "bangChungFileKey")]
    [InlineData("Scheduling.ScheduleQuery", "maNhanVien")]
    public void OptionalField_ExplicitNullRejected(string name, string field)
    {
        var valid = ValidRequests.Single(row => (string)row[0] == name)[1].ToString()!;
        var json = valid[..^1] + $",\"{field}\":null}}";
        Assert.Throws<JsonException>(() => Deserialize(name, json));
    }

    [Theory]
    [InlineData("Booking.PetEdit")]
    [InlineData("Billing.FeeEdit")]
    public void Patch_RejectsUnknownFieldsAndNoBusinessChanges(string name)
    {
        Assert.Throws<JsonException>(() => Deserialize(name, Command("\"actorId\":1")));
        Assert.NotEmpty(Errors(Deserialize(name, Command("\"lyDo\":\"Lý do\"").Replace(",\"lyDo\":\"Lý do\"", name.EndsWith("PetEdit") ? "" : ",\"lyDo\":\"Lý do\""))));
    }

    [Theory]
    [InlineData("Billing.ReceiptCreate", "\"soTien\":0,\"phuongThuc\":\"CASH\",\"giaoDichAt\":\"2035-01-01T00:00:00Z\"")]
    [InlineData("Billing.RefundCreate", "\"soTien\":1.5,\"lyDo\":\"Hoàn\"")]
    [InlineData("Billing.RefundCreate", "\"soTien\":100000000000000,\"lyDo\":\"Hoàn\"")]
    [InlineData("Booking.PetEdit", "\"canNang\":0")]
    [InlineData("Booking.PetEdit", "\"canNang\":1.234")]
    [InlineData("Booking.PetCancelRequest", "\"soDemDaO\":-1,\"lyDo\":\"Hủy\",\"maThanhPhanDaLam\":[]")]
    [InlineData("Billing.ExtraServiceRequest", "\"maDichVu\":1,\"maCombo\":2,\"maKhungGio\":1,\"lyDo\":\"Thêm\"")]
    [InlineData("Billing.ExtraServiceRequest", "\"maKhungGio\":1,\"lyDo\":\"Thêm\"")]
    [InlineData("Billing.ServiceActionRequest", "\"hanhDong\":\"CANCEL\"")]
    [InlineData("Billing.ServiceActionRequest", "\"hanhDong\":\"complete\"")]
    public void Requests_RejectInvalidBusinessValues(string name, string properties) =>
        Assert.NotEmpty(Errors(Deserialize(name, Command(properties))));

    [Theory, MemberData(nameof(ValidRequests))]
    public void RequiredFields_CannotBeOmitted(string name, string json)
    {
        if (!json.Contains("requestId")) return;
        Assert.Throws<JsonException>(() => Deserialize(name, json.Replace($"\"requestId\":\"{Id}\",", "")));
        Assert.NotEmpty(Errors(Deserialize(name, json.Replace(Id, Guid.Empty.ToString()))));
    }

    [Fact]
    public void ScheduleQuery_RejectsReversedDatesWithoutRejectingEmployeeFilter()
    {
        Assert.NotEmpty(Errors(Deserialize("Scheduling.ScheduleQuery", "{\"tuNgay\":\"2035-02-01\",\"denNgay\":\"2035-01-01\"}")));
        Assert.Empty(Errors(Deserialize("Scheduling.ScheduleQuery", "{\"tuNgay\":\"2035-01-01\",\"denNgay\":\"2035-01-02\",\"maNhanVien\":1}")));
    }

    [Theory]
    [InlineData("Billing.DepositDto", "maTienCoc,maDon,trangThaiYeuCau,coSoTinh,tyLe,soTienYeuCau,hetHanAt,thucNhan,khauTru,giuLai,danhHoan,daHoan,hoanCoc")]
    [InlineData("Billing.RefundDto", "maHoanCoc,maTienCoc,soTien,lyDo,trangThai,hoanAt,thamChieu")]
    [InlineData("Billing.PageDeposit", "items,trang,kichThuocTrang,tongSo")]
    [InlineData("Billing.PageRefund", "items,trang,kichThuocTrang,tongSo")]
    [InlineData("Billing.FeeDto", "maPhuPhi,maDon,maChiTietDon,tenPhuPhi,soTien,lyDo,huyAt")]
    [InlineData("Booking.CustomerDto", "maKhachHang,hoTen,soDienThoai,email,thuCung")]
    [InlineData("Booking.PetDto", "maThuCung,tenThuCung,loaiPet,giong,canNang,ngaySinh,loaiLong,ghiChuDiUng")]
    [InlineData("Booking.AcknowledgedResponse", "daTiepNhan,thongBao")]
    [InlineData("Scheduling.ShiftRegistrationDto", "maDangKy,maNhanVien,ngay,maCa,trangThai,lyDoDieuChuyen")]
    [InlineData("Scheduling.ShiftWindowDto", "maDot,tuNgay,denNgay,moAt,dongAt")]
    public void Response_OnlyExposesContractProperties(string name, string expected)
    {
        var type = typeof(BookingQuery).Assembly.GetType("PetSpa.Core.DTOs." + name);
        Assert.NotNull(type);
        Assert.Equal(expected.Split(',').Order(), type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order());
    }

    private static string Command(string properties) => $"{{\"requestId\":\"{Id}\",{properties}}}";
    private static object Deserialize(string name, string json)
    {
        var type = typeof(BookingQuery).Assembly.GetType("PetSpa.Core.DTOs." + name);
        Assert.NotNull(type);
        return JsonSerializer.Deserialize(json, type, Options)!;
    }
    private static List<ValidationResult> Errors(object request)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
        return errors;
    }
}
