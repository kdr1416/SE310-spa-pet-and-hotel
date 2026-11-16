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

            migrationBuilder.AlterColumn<string>(
                name: "tinh_trang_long",
                table: "chi_tiet_don",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tinh_trang_long",
                table: "chi_tiet_don",
                type: "character varying(40)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_chi_tiet_don_tinh_trang_long_values",
                table: "chi_tiet_don",
                sql: "tinh_trang_long IN ('BINH_THUONG', 'ROI_NHE', 'ROI_NANG', 'VE_RAN')");
        }
    }
}
