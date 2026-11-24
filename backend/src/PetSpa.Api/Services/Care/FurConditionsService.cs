using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;

namespace PetSpa.Api.Services.Care;

public sealed class FurConditionsService(PetSpaDbContext db, CareOperation care)
{
    public async Task<ApiResponse<IReadOnlyList<FurConditionItem>>> GetAsync(int actorId, CancellationToken cancellationToken)
    {
        try
        {
            if (await care.FindEmployeeWithGrantAsync(actorId, "CHECKIN_PERFORM", cancellationToken) is null)
                return ApiResponse<IReadOnlyList<FurConditionItem>>.Fail("Bạn không có quyền tiếp nhận.", "FORBIDDEN");
            var raw = await db.CauHinhHeThongSet.AsNoTracking().Where(x => x.Khoa == "FUR_CONDITIONS")
                .Select(x => x.GiaTri).SingleOrDefaultAsync(cancellationToken);
            try
            {
                var items = raw is null ? null : JsonSerializer.Deserialize<List<FurConditionItem>>(raw, CareOperation.JsonOptions);
                if (items is not null && items.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Ma) && !string.IsNullOrWhiteSpace(x.Ten)) &&
                    items.Select(x => x.Ma).Distinct(StringComparer.Ordinal).Count() == items.Count)
                    return ApiResponse<IReadOnlyList<FurConditionItem>>.Ok(items.Where(x => x.DangHoatDong).ToArray());
            }
            catch (JsonException) { }
            return ApiResponse<IReadOnlyList<FurConditionItem>>.Fail("Danh mục tình trạng lông tạm thời không khả dụng.", "TEMPORARY_FAILURE");
        }
        catch (Exception error) when (error is NpgsqlException { IsTransient: true } or
            InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } })
        {
            return ApiResponse<IReadOnlyList<FurConditionItem>>.Fail("Dịch vụ tạm thời không khả dụng.", "TEMPORARY_FAILURE");
        }
    }
}
