using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PetSpa.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PetSpa.Tests;

public class DatabaseModelTests
{
    // Npgsql model only: no connection string and no database access.
    private static PetSpaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PetSpaDbContext>().UseNpgsql().Options);

    [Fact]
    public void Model_ContainsExactlyThe33ContractTables()
    {
        using var context = CreateContext();
        string[] expected = [
            "nhan_vien", "quyen", "nhan_vien_quyen", "otp_challenge", "bao_ve_tracking", "audit_log",
            "khach_hang", "thu_cung", "don_dat_lich", "chi_tiet_don",
            "ho_so_tiem_chung", "nhat_ky_cham_soc", "danh_gia",
            "dich_vu", "combo", "chi_tiet_combo", "bang_gia_size", "he_so_can_nang", "loai_phong",
            "dich_vu_trong_don", "thanh_phan_dich_vu_trong_don", "phu_phi", "hoa_don", "ct_hoa_don",
            "tien_coc", "giao_dich_thu", "hoan_coc",
            "ca_lam_viec", "khung_gio_dat_lich", "dang_ky_ca", "ngay_nghi", "dot_dang_ky_ca", "cau_hinh_he_thong"
        ];
        Assert.Equal(expected.Order(), context.Model.GetEntityTypes().Select(e => e.GetTableName()!).Order());
    }

    [Theory]
    [InlineData("don_dat_lich", "request_id")]
    [InlineData("don_dat_lich", "tracking_code")]
    [InlineData("hoa_don", "ma_hoa_don")]
    [InlineData("tien_coc", "don_dat_lich_id")]
    [InlineData("nhan_vien", "ten_dang_nhap")]
    public void BusinessIdentifiers_HaveUniqueIndexes(string table, string column)
    {
        using var context = CreateContext();
        var entity = context.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var store = StoreObjectIdentifier.Table(table, null);
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.GetColumnName(store)).SequenceEqual([column]));
    }

    [Fact]
    public void EveryForeignKey_RestrictsDeletion_AndHasAnInverseNavigation()
    {
        using var context = CreateContext();
        var keys = context.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).ToArray();
        Assert.NotEmpty(keys);
        Assert.All(keys, fk =>
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
            Assert.NotNull(fk.DependentToPrincipal);
            Assert.NotNull(fk.PrincipalToDependent);
            Assert.DoesNotContain(fk.Properties, p => p.IsShadowProperty());
        });
    }

    [Theory]
    [InlineData("chi_tiet_combo", "combo_id", "dich_vu_id")]
    [InlineData("nhan_vien_quyen", "nhan_vien_id", "ma_quyen")]
    public void JoinTables_HaveTheRequestedCompositeKeys(string table, string first, string second)
    {
        using var context = CreateContext();
        var entity = context.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var store = StoreObjectIdentifier.Table(table, null);
        Assert.Equal(new[] { first, second }, entity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(store)));
    }

    [Fact]
    public void Invoice_EnforcesOneEffectiveVersionAndUniqueReplacement()
    {
        using var context = CreateContext();
        var invoice = context.Model.GetEntityTypes().Single(e => e.GetTableName() == "hoa_don");
        Assert.Contains(invoice.GetIndexes(), i => i.IsUnique && i.GetFilter() == "trang_thai <> 'REVOKED'");
        Assert.Contains(invoice.GetIndexes(), i => i.IsUnique && i.GetFilter() == "thay_the_hoa_don_id IS NOT NULL");
    }

    [Theory]
    [InlineData("ct_hoa_don", "don_gia_snapshot", "numeric(14,0)", false)]
    [InlineData("chi_tiet_don", "can_nang_chot", "numeric(5,2)", true)]
    [InlineData("don_dat_lich", "chinh_sach_snapshot", "jsonb", false)]
    [InlineData("dich_vu_trong_don", "bang_gia_size_snapshot", "jsonb", false)]
    [InlineData("hoa_don", "khach_snapshot", "jsonb", false)]
    [InlineData("tien_coc", "het_han_at", "timestamp with time zone", false)]
    [InlineData("khung_gio_dat_lich", "ngay", "date", false)]
    public void Columns_PreservePostgresTypesAndNullability(string table, string column, string type, bool nullable)
    {
        using var context = CreateContext();
        var entity = context.Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var store = StoreObjectIdentifier.Table(table, null);
        var property = entity.GetProperties().Single(p => p.GetColumnName(store) == column);
        Assert.Equal(type, property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
    }

    [Fact]
    public void InitialMigration_GeneratesAllTablesOffline_WithRestrictiveForeignKeys()
    {
        using var context = CreateContext();
        Assert.Contains(context.Database.GetMigrations(), m => m.EndsWith("_InitialCoreSchema"));
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript();
        Assert.Equal(33, System.Text.RegularExpressions.Regex.Matches(sql, "CREATE TABLE (?!IF NOT EXISTS)").Count);
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("ON DELETE CASCADE", sql);
        Assert.Contains("trang_thai <> 'REVOKED'", sql);
    }
    [Fact]
    public void AnonymousCommands_HaveANullSafeIdempotencyIndex()
    {
        using var context = CreateContext();
        var audit = context.Model.GetEntityTypes().Single(e => e.GetTableName() == "audit_log");
        Assert.Contains(audit.GetIndexes(), i => i.IsUnique &&
            i.GetFilter() == "actor_id IS NULL AND request_id IS NOT NULL" &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "HanhDong", "RequestId" }));
    }

    [Fact]
    public void ApiContainer_ResolvesTheContextWithoutOpeningADatabase()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(33, context.Model.GetEntityTypes().Count());
    }
    [Fact]
    public void ApiContainer_UsesTheExistingRuntimeConnectionKey_WithoutConnecting()
    {
        const string localConnection = "Host=127.0.0.1;Port=6543;Database=petspa_schema_test";
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = localConnection })));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        Assert.Equal(localConnection, context.Database.GetConnectionString());
    }
}
