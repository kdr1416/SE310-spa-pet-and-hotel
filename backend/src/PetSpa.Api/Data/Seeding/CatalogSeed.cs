using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Data.Seeding;

public static class CatalogSeed
{
    private const string FurConditionsKey = "FUR_CONDITIONS";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly FurConditionItem[] Defaults =
    [
        Condition("BINH_THUONG", "Bình thường"),
        Condition("ROI_NHE", "Rối nhẹ"),
        Condition("ROI_NANG", "Rối nặng", allowsDetanglingFee: true),
        Condition("VE_RAN", "Ve rận"),
        Condition("LONG_NGAN", "Lông ngắn"),
        Condition("LONG_DAI", "Lông dài")
    ];

    public static async Task InitializeAsync(PetSpaDbContext context, int adminId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var adminExists = await context.NhanVienSet.AnyAsync(
            employee => employee.Id == adminId && employee.IsActive && employee.VaiTro == Role.ADMIN, cancellationToken);
        if (!adminExists)
            throw new InvalidOperationException("CatalogSeed:AdminId must reference an active Admin.");

        await EnsureFurConditionsAsync(context, adminId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task EnsureFurConditionsAsync(
        PetSpaDbContext context, int adminId, CancellationToken cancellationToken)
    {
        var defaultsJson = JsonSerializer.Serialize(Defaults, JsonOptions);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cau_hinh_he_thong (khoa, gia_tri, phien_ban, sua_boi_id)
            VALUES ({FurConditionsKey}, CAST({defaultsJson} AS jsonb), 1, {adminId})
            ON CONFLICT (khoa) DO NOTHING
            """, cancellationToken);
        if (inserted != 0)
            return;

        var rows = await context.CauHinhHeThongSet.FromSqlInterpolated($"""
            SELECT * FROM cau_hinh_he_thong WHERE khoa = {FurConditionsKey} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var row = rows.Single();
        if (JsonNode.Parse(row.GiaTri) is not JsonArray values)
            throw new InvalidOperationException("FUR_CONDITIONS must be a JSON array.");

        var existingCodes = values.OfType<JsonObject>()
            .Select(item => item["ma"]?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var missing = Defaults.Where(item => !existingCodes.Contains(item.Ma)).ToArray();
        if (missing.Length == 0)
            return;

        foreach (var condition in missing)
            values.Add(JsonSerializer.SerializeToNode(condition, JsonOptions));
        row.GiaTri = values.ToJsonString(JsonOptions);
        row.PhienBan++;
        row.SuaBoiId = adminId;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static FurConditionItem Condition(string code, string name, bool allowsDetanglingFee = false) =>
        new() { Ma = code, Ten = name, DangHoatDong = true, ChoPhepPhuPhiGoRoi = allowsDetanglingFee };
}
