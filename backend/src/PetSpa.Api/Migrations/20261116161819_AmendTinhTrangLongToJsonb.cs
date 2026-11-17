using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetSpa.Api.Migrations
{
    /// <inheritdoc />
    public partial class AmendTinhTrangLongToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_chi_tiet_don_tinh_trang_long_values",
                table: "chi_tiet_don");

            migrationBuilder.Sql("""
                ALTER TABLE chi_tiet_don
                ALTER COLUMN tinh_trang_long TYPE jsonb
                USING CASE
                    WHEN tinh_trang_long IS NULL THEN NULL
                    ELSE jsonb_build_array(tinh_trang_long)
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE chi_tiet_don
                ALTER COLUMN tinh_trang_long TYPE character varying(40)
                USING CASE
                    WHEN tinh_trang_long IS NULL THEN NULL
                    WHEN jsonb_typeof(tinh_trang_long) = 'array' THEN tinh_trang_long->>0
                    ELSE tinh_trang_long #>> '{}'
                END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_chi_tiet_don_tinh_trang_long_values",
                table: "chi_tiet_don",
                sql: "tinh_trang_long IN ('BINH_THUONG', 'ROI_NHE', 'ROI_NANG', 'VE_RAN')");
        }
    }
}
