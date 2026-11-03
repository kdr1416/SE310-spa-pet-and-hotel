using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetSpa.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ca_lam_viec",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_ca = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    gio_bat_dau = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    gio_ket_thuc = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    suc_chua_pet = table.Column<int>(type: "integer", nullable: false),
                    tran_phut = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ca_lam_viec", x => x.id);
                    table.CheckConstraint("ck_ca_lam_viec_gio", "gio_ket_thuc > gio_bat_dau");
                    table.CheckConstraint("ck_ca_lam_viec_suc_chua", "suc_chua_pet >= 0 AND tran_phut >= 0");
                });

            migrationBuilder.CreateTable(
                name: "combo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_combo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    mo_ta = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_combo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dich_vu",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_dich_vu = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    loai_dich_vu = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    thoi_luong_phut = table.Column<int>(type: "integer", nullable: false),
                    yeu_cau_tiem_chung = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    mo_ta = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dich_vu", x => x.id);
                    table.CheckConstraint("ck_dich_vu_thoi_luong", "thoi_luong_phut > 0");
                });

            migrationBuilder.CreateTable(
                name: "he_so_can_nang",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ma_size = table.Column<string>(type: "character varying(40)", nullable: false),
                    can_nang_tu = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    can_nang_den = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_he_so_can_nang", x => x.id);
                    table.CheckConstraint("ck_he_so_can_nang_khoang", "can_nang_tu >= 0 AND (can_nang_den IS NULL OR can_nang_den > can_nang_tu)");
                    table.CheckConstraint("ck_he_so_can_nang_ma_size_values", "ma_size IN ('S', 'M', 'L', 'XL')");
                });

            migrationBuilder.CreateTable(
                name: "khach_hang",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ho_ten = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    so_dien_thoai = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_khach_hang", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loai_phong",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_phong = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    gia_moi_dem = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    so_luong_phong = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loai_phong", x => x.id);
                    table.CheckConstraint("ck_loai_phong_gia_moi_dem_nonnegative", "gia_moi_dem >= 0");
                    table.CheckConstraint("ck_loai_phong_suc_chua", "so_luong_phong >= 0");
                });

            migrationBuilder.CreateTable(
                name: "nhan_vien",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_nhan_vien = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ten_dang_nhap = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mat_khau_hash = table.Column<string>(type: "text", nullable: false),
                    vai_tro = table.Column<string>(type: "character varying(40)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nhan_vien", x => x.id);
                    table.CheckConstraint("ck_nhan_vien_vai_tro_values", "vai_tro IN ('ADMIN', 'STAFF')");
                });

            migrationBuilder.CreateTable(
                name: "quyen",
                columns: table => new
                {
                    ma_quyen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ten_quyen = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quyen", x => x.ma_quyen);
                });

            migrationBuilder.CreateTable(
                name: "khung_gio_dat_lich",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ngay = table.Column<DateOnly>(type: "date", nullable: false),
                    mo_nhan_khach = table.Column<bool>(type: "boolean", nullable: false),
                    tong_slot = table.Column<int>(type: "integer", nullable: false),
                    slot_da_dat = table.Column<int>(type: "integer", nullable: false),
                    tran_phut = table.Column<int>(type: "integer", nullable: false),
                    phut_da_giu = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ca_lam_viec_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_khung_gio_dat_lich", x => x.id);
                    table.CheckConstraint("ck_khung_gio_dat_lich_phut", "tran_phut >= 0 AND phut_da_giu >= 0 AND phut_da_giu <= tran_phut");
                    table.CheckConstraint("ck_khung_gio_dat_lich_slot", "tong_slot >= 0 AND slot_da_dat >= 0 AND slot_da_dat <= tong_slot");
                    table.ForeignKey(
                        name: "fk_khung_gio_dat_lich_ca_lam_viec_id",
                        column: x => x.ca_lam_viec_id,
                        principalTable: "ca_lam_viec",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chi_tiet_combo",
                columns: table => new
                {
                    combo_id = table.Column<int>(type: "integer", nullable: false),
                    dich_vu_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chi_tiet_combo", x => new { x.combo_id, x.dich_vu_id });
                    table.ForeignKey(
                        name: "fk_chi_tiet_combo_combo_id",
                        column: x => x.combo_id,
                        principalTable: "combo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_combo_dich_vu_id",
                        column: x => x.dich_vu_id,
                        principalTable: "dich_vu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bang_gia_size",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    don_gia = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    dich_vu_id = table.Column<int>(type: "integer", nullable: true),
                    combo_id = table.Column<int>(type: "integer", nullable: true),
                    size_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bang_gia_size", x => x.id);
                    table.CheckConstraint("ck_bang_gia_size_don_gia_nonnegative", "don_gia >= 0");
                    table.CheckConstraint("ck_bang_gia_size_nguon", "(dich_vu_id IS NOT NULL) <> (combo_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_bang_gia_size_combo_id",
                        column: x => x.combo_id,
                        principalTable: "combo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bang_gia_size_dich_vu_id",
                        column: x => x.dich_vu_id,
                        principalTable: "dich_vu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bang_gia_size_size_id",
                        column: x => x.size_id,
                        principalTable: "he_so_can_nang",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenge",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    muc_dich = table.Column<string>(type: "character varying(40)", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    so_dien_thoai = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    otp_digest = table.Column<string>(type: "text", nullable: false),
                    het_han_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gui_lai_sau = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    so_lan_sai = table.Column<int>(type: "integer", nullable: false),
                    su_dung_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proof_digest = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    khach_hang_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenge", x => x.id);
                    table.CheckConstraint("ck_otp_challenge_muc_dich_values", "muc_dich IN ('BOOKING', 'EXISTING_PETS')");
                    table.ForeignKey(
                        name: "fk_otp_challenge_khach_hang_id",
                        column: x => x.khach_hang_id,
                        principalTable: "khach_hang",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "thu_cung",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_thu_cung = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    loai_pet = table.Column<string>(type: "character varying(40)", nullable: false),
                    can_nang = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    giong = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ngay_sinh = table.Column<DateOnly>(type: "date", nullable: true),
                    loai_long = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ghi_chu_di_ung = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    khach_hang_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thu_cung", x => x.id);
                    table.CheckConstraint("ck_thu_cung_can_nang", "can_nang > 0");
                    table.CheckConstraint("ck_thu_cung_loai_pet_values", "loai_pet IN ('CHO', 'MEO')");
                    table.ForeignKey(
                        name: "fk_thu_cung_khach_hang_id",
                        column: x => x.khach_hang_id,
                        principalTable: "khach_hang",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_loai = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    hanh_dong = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    loai_doi_tuong = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trace_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    doi_tuong_id = table.Column<int>(type: "integer", nullable: true),
                    ly_do = table.Column<string>(type: "text", nullable: true),
                    truoc = table.Column<string>(type: "jsonb", nullable: true),
                    sau = table.Column<string>(type: "jsonb", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "text", nullable: true),
                    ket_qua = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    actor_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_actor_id",
                        column: x => x.actor_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bao_ve_tracking",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    khoa_ip_hash = table.Column<string>(type: "text", nullable: false),
                    khoa_sdt_hash = table.Column<string>(type: "text", nullable: false),
                    so_lan_sai = table.Column<int>(type: "integer", nullable: false),
                    bat_dau_cua_so = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    khoa_den = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mo_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    mo_boi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bao_ve_tracking", x => x.id);
                    table.ForeignKey(
                        name: "fk_bao_ve_tracking_mo_boi_id",
                        column: x => x.mo_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cau_hinh_he_thong",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    khoa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gia_tri = table.Column<string>(type: "jsonb", nullable: false),
                    phien_ban = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    sua_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cau_hinh_he_thong", x => x.id);
                    table.ForeignKey(
                        name: "fk_cau_hinh_he_thong_sua_boi_id",
                        column: x => x.sua_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dot_dang_ky_ca",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tu_ngay = table.Column<DateOnly>(type: "date", nullable: false),
                    den_ngay = table.Column<DateOnly>(type: "date", nullable: false),
                    mo_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dong_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dot_dang_ky_ca", x => x.id);
                    table.CheckConstraint("ck_dot_dang_ky_ca_khoang", "tu_ngay <= den_ngay AND mo_at < dong_at");
                    table.ForeignKey(
                        name: "fk_dot_dang_ky_ca_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ngay_nghi",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ngay = table.Column<DateOnly>(type: "date", nullable: false),
                    ly_do = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ngay_nghi", x => x.id);
                    table.ForeignKey(
                        name: "fk_ngay_nghi_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nhan_vien_quyen",
                columns: table => new
                {
                    nhan_vien_id = table.Column<int>(type: "integer", nullable: false),
                    ma_quyen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cap_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    cap_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nhan_vien_quyen", x => new { x.nhan_vien_id, x.ma_quyen });
                    table.ForeignKey(
                        name: "fk_nhan_vien_quyen_cap_boi_id",
                        column: x => x.cap_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nhan_vien_quyen_ma_quyen",
                        column: x => x.ma_quyen,
                        principalTable: "quyen",
                        principalColumn: "ma_quyen",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nhan_vien_quyen_nhan_vien_id",
                        column: x => x.nhan_vien_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "don_dat_lich",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    tracking_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    ten_nguoi_dat = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    so_dien_thoai_snapshot = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    email_snapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    bat_dau_du_kien = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ket_thuc_du_kien = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    moc_cuoi_ca = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tong_tam_tinh = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    chinh_sach_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    email_delivery = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duyet_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    huy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hoan_tat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    loai_huy = table.Column<string>(type: "character varying(40)", nullable: true),
                    ly_do_huy = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    khach_hang_id = table.Column<int>(type: "integer", nullable: false),
                    khung_gio_id = table.Column<int>(type: "integer", nullable: false),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: true),
                    duyet_boi_id = table.Column<int>(type: "integer", nullable: true),
                    huy_boi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_don_dat_lich", x => x.id);
                    table.CheckConstraint("ck_don_dat_lich_loai_huy_values", "loai_huy IN ('CUSTOMER_CANCELLED', 'STAFF_REJECTED', 'NO_SHOW', 'STAFF_CANCELLED')");
                    table.CheckConstraint("ck_don_dat_lich_ngay_du_kien", "ket_thuc_du_kien >= bat_dau_du_kien");
                    table.CheckConstraint("ck_don_dat_lich_tong_tam_tinh_nonnegative", "tong_tam_tinh >= 0");
                    table.CheckConstraint("ck_don_dat_lich_trang_thai_values", "trang_thai IN ('PENDING', 'CONFIRMED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_don_dat_lich_duyet_boi_id",
                        column: x => x.duyet_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_dat_lich_huy_boi_id",
                        column: x => x.huy_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_dat_lich_khach_hang_id",
                        column: x => x.khach_hang_id,
                        principalTable: "khach_hang",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_dat_lich_khung_gio_id",
                        column: x => x.khung_gio_id,
                        principalTable: "khung_gio_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_don_dat_lich_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ho_so_tiem_chung",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loai_vaccine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ngay_tiem = table.Column<DateOnly>(type: "date", nullable: false),
                    hieu_luc_den = table.Column<DateOnly>(type: "date", nullable: true),
                    object_path = table.Column<string>(type: "text", nullable: true),
                    co_so_cap = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ghi_chu = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    thu_cung_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ho_so_tiem_chung", x => x.id);
                    table.CheckConstraint("ck_ho_so_tiem_chung_hieu_luc", "hieu_luc_den IS NULL OR hieu_luc_den >= ngay_tiem");
                    table.ForeignKey(
                        name: "fk_ho_so_tiem_chung_thu_cung_id",
                        column: x => x.thu_cung_id,
                        principalTable: "thu_cung",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dang_ky_ca",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ngay = table.Column<DateOnly>(type: "date", nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    duyet_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ly_do_dieu_chuyen = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    nhan_vien_id = table.Column<int>(type: "integer", nullable: false),
                    ca_lam_viec_id = table.Column<int>(type: "integer", nullable: false),
                    dot_id = table.Column<int>(type: "integer", nullable: false),
                    duyet_boi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dang_ky_ca", x => x.id);
                    table.CheckConstraint("ck_dang_ky_ca_trang_thai_values", "trang_thai IN ('PENDING', 'APPROVED', 'REJECTED')");
                    table.ForeignKey(
                        name: "fk_dang_ky_ca_ca_lam_viec_id",
                        column: x => x.ca_lam_viec_id,
                        principalTable: "ca_lam_viec",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dang_ky_ca_dot_id",
                        column: x => x.dot_id,
                        principalTable: "dot_dang_ky_ca",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dang_ky_ca_duyet_boi_id",
                        column: x => x.duyet_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dang_ky_ca_nhan_vien_id",
                        column: x => x.nhan_vien_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hoa_don",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ma_hoa_don = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    tong_tien = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    coc_khau_tru = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    con_phai_tra = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    khach_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    thu_hoi_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ly_do_thu_hoi = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    don_dat_lich_id = table.Column<int>(type: "integer", nullable: false),
                    thay_the_hoa_don_id = table.Column<int>(type: "integer", nullable: true),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: false),
                    thu_boi_id = table.Column<int>(type: "integer", nullable: true),
                    thu_hoi_boi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hoa_don", x => x.id);
                    table.CheckConstraint("ck_hoa_don_coc_khau_tru_nonnegative", "coc_khau_tru >= 0");
                    table.CheckConstraint("ck_hoa_don_con_phai_tra_nonnegative", "con_phai_tra >= 0");
                    table.CheckConstraint("ck_hoa_don_khong_tu_thay_the", "thay_the_hoa_don_id IS NULL OR thay_the_hoa_don_id <> id");
                    table.CheckConstraint("ck_hoa_don_tong_tien", "tong_tien >= coc_khau_tru AND con_phai_tra = tong_tien - coc_khau_tru");
                    table.CheckConstraint("ck_hoa_don_tong_tien_nonnegative", "tong_tien >= 0");
                    table.CheckConstraint("ck_hoa_don_trang_thai_values", "trang_thai IN ('UNPAID', 'PAID', 'REVOKED')");
                    table.ForeignKey(
                        name: "fk_hoa_don_don_dat_lich_id",
                        column: x => x.don_dat_lich_id,
                        principalTable: "don_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoa_don_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoa_don_thay_the_hoa_don_id",
                        column: x => x.thay_the_hoa_don_id,
                        principalTable: "hoa_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoa_don_thu_boi_id",
                        column: x => x.thu_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoa_don_thu_hoi_boi_id",
                        column: x => x.thu_hoi_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tien_coc",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    co_so_tinh_snapshot = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    ty_le = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    so_tien_yeu_cau = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    het_han_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trang_thai_yeu_cau = table.Column<string>(type: "character varying(40)", nullable: false),
                    ket_thuc_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    don_dat_lich_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tien_coc", x => x.id);
                    table.CheckConstraint("ck_tien_coc_co_so_tinh_snapshot_nonnegative", "co_so_tinh_snapshot >= 0");
                    table.CheckConstraint("ck_tien_coc_so_tien_yeu_cau_nonnegative", "so_tien_yeu_cau >= 0");
                    table.CheckConstraint("ck_tien_coc_trang_thai_yeu_cau_values", "trang_thai_yeu_cau IN ('CHO_THU', 'DA_DAP_UNG', 'HET_HAN', 'HUY')");
                    table.CheckConstraint("ck_tien_coc_ty_le", "ty_le = 0.30");
                    table.ForeignKey(
                        name: "fk_tien_coc_don_dat_lich_id",
                        column: x => x.don_dat_lich_id,
                        principalTable: "don_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chi_tiet_don",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_pet_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trang_thai_cham_soc = table.Column<string>(type: "character varying(40)", nullable: false, defaultValue: "NOT_ARRIVED"),
                    can_nang_chot = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    size_thuc_te = table.Column<string>(type: "character varying(40)", nullable: true),
                    size_chot = table.Column<string>(type: "character varying(40)", nullable: true),
                    thoa_thuan_gia = table.Column<string>(type: "text", nullable: true),
                    gia_phong_cam_ket = table.Column<decimal>(type: "numeric(14,0)", nullable: true),
                    gia_phong_chot = table.Column<decimal>(type: "numeric(14,0)", nullable: true),
                    so_dem_chot = table.Column<int>(type: "integer", nullable: true),
                    check_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ban_giao_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tra_lai_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nhan_som = table.Column<bool>(type: "boolean", nullable: false),
                    tinh_trang_long = table.Column<string>(type: "character varying(40)", nullable: true),
                    ghi_chu_suc_khoe = table.Column<string>(type: "text", nullable: true),
                    xac_minh_tiem_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    huy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ly_do_huy = table.Column<string>(type: "text", nullable: true),
                    so_dem_da_o = table.Column<int>(type: "integer", nullable: true),
                    nhan_som_ly_do = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    don_dat_lich_id = table.Column<int>(type: "integer", nullable: false),
                    thu_cung_id = table.Column<int>(type: "integer", nullable: false),
                    loai_phong_id = table.Column<int>(type: "integer", nullable: true),
                    nhan_vien_id = table.Column<int>(type: "integer", nullable: true),
                    ho_so_tiem_chung_id = table.Column<int>(type: "integer", nullable: true),
                    xac_minh_tiem_boi_id = table.Column<int>(type: "integer", nullable: true),
                    check_in_boi_id = table.Column<int>(type: "integer", nullable: true),
                    ban_giao_boi_id = table.Column<int>(type: "integer", nullable: true),
                    huy_boi_id = table.Column<int>(type: "integer", nullable: true),
                    nhan_som_boi_id = table.Column<int>(type: "integer", nullable: true),
                    can_nang_khai_bao = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chi_tiet_don", x => x.id);
                    table.CheckConstraint("ck_chi_tiet_don_can_nang_chot_positive", "can_nang_chot > 0");
                    table.CheckConstraint("ck_chi_tiet_don_can_nang_khai_bao_positive", "can_nang_khai_bao > 0");
                    table.CheckConstraint("ck_chi_tiet_don_gia_phong_cam_ket_nonnegative", "gia_phong_cam_ket >= 0");
                    table.CheckConstraint("ck_chi_tiet_don_gia_phong_chot_nonnegative", "gia_phong_chot >= 0");
                    table.CheckConstraint("ck_chi_tiet_don_size_chot_values", "size_chot IN ('S', 'M', 'L', 'XL')");
                    table.CheckConstraint("ck_chi_tiet_don_size_thuc_te_values", "size_thuc_te IN ('S', 'M', 'L', 'XL')");
                    table.CheckConstraint("ck_chi_tiet_don_tinh_trang_long_values", "tinh_trang_long IN ('BINH_THUONG', 'ROI_NHE', 'ROI_NANG', 'VE_RAN')");
                    table.CheckConstraint("ck_chi_tiet_don_trang_thai_cham_soc_values", "trang_thai_cham_soc IN ('NOT_ARRIVED', 'WAITING', 'IN_PROGRESS', 'PAUSED', 'READY', 'DONE', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_ban_giao_boi_id",
                        column: x => x.ban_giao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_check_in_boi_id",
                        column: x => x.check_in_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_don_dat_lich_id",
                        column: x => x.don_dat_lich_id,
                        principalTable: "don_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_ho_so_tiem_chung_id",
                        column: x => x.ho_so_tiem_chung_id,
                        principalTable: "ho_so_tiem_chung",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_huy_boi_id",
                        column: x => x.huy_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_loai_phong_id",
                        column: x => x.loai_phong_id,
                        principalTable: "loai_phong",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_nhan_som_boi_id",
                        column: x => x.nhan_som_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_nhan_vien_id",
                        column: x => x.nhan_vien_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_thu_cung_id",
                        column: x => x.thu_cung_id,
                        principalTable: "thu_cung",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chi_tiet_don_xac_minh_tiem_boi_id",
                        column: x => x.xac_minh_tiem_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "giao_dich_thu",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    so_tien = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    phuong_thuc = table.Column<string>(type: "character varying(40)", nullable: false),
                    ngan_hang = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ma_giao_dich = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    giao_dich_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xac_nhan_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ghi_chu = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    tien_coc_id = table.Column<int>(type: "integer", nullable: true),
                    hoa_don_id = table.Column<int>(type: "integer", nullable: true),
                    xac_nhan_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_giao_dich_thu", x => x.id);
                    table.CheckConstraint("ck_giao_dich_thu_dich_thu", "(tien_coc_id IS NOT NULL) <> (hoa_don_id IS NOT NULL)");
                    table.CheckConstraint("ck_giao_dich_thu_phuong_thuc_values", "phuong_thuc IN ('CASH', 'BANK_TRANSFER')");
                    table.CheckConstraint("ck_giao_dich_thu_so_tien", "so_tien > 0");
                    table.CheckConstraint("ck_giao_dich_thu_so_tien_nonnegative", "so_tien >= 0");
                    table.ForeignKey(
                        name: "fk_giao_dich_thu_hoa_don_id",
                        column: x => x.hoa_don_id,
                        principalTable: "hoa_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_giao_dich_thu_tien_coc_id",
                        column: x => x.tien_coc_id,
                        principalTable: "tien_coc",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_giao_dich_thu_xac_nhan_boi_id",
                        column: x => x.xac_nhan_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hoan_coc",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    so_tien = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    ly_do = table.Column<string>(type: "text", nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    hoan_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    phuong_thuc = table.Column<string>(type: "character varying(40)", nullable: true),
                    tham_chieu = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    bang_chung_path = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    tien_coc_id = table.Column<int>(type: "integer", nullable: false),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: false),
                    hoan_boi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hoan_coc", x => x.id);
                    table.CheckConstraint("ck_hoan_coc_phuong_thuc_values", "phuong_thuc IN ('CASH', 'BANK_TRANSFER')");
                    table.CheckConstraint("ck_hoan_coc_so_tien", "so_tien > 0");
                    table.CheckConstraint("ck_hoan_coc_so_tien_nonnegative", "so_tien >= 0");
                    table.CheckConstraint("ck_hoan_coc_trang_thai_values", "trang_thai IN ('CHO_HOAN', 'DA_HOAN')");
                    table.ForeignKey(
                        name: "fk_hoan_coc_hoan_boi_id",
                        column: x => x.hoan_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoan_coc_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hoan_coc_tien_coc_id",
                        column: x => x.tien_coc_id,
                        principalTable: "tien_coc",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dich_vu_trong_don",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    la_phat_sinh = table.Column<bool>(type: "boolean", nullable: false),
                    ten_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    bang_gia_size_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    thoi_luong_snapshot = table.Column<int>(type: "integer", nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    size_chot = table.Column<string>(type: "character varying(40)", nullable: true),
                    gia_chot = table.Column<decimal>(type: "numeric(14,0)", nullable: true),
                    ly_do = table.Column<string>(type: "text", nullable: true),
                    hoan_tat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    huy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    chi_tiet_don_id = table.Column<int>(type: "integer", nullable: false),
                    dich_vu_id = table.Column<int>(type: "integer", nullable: true),
                    combo_id = table.Column<int>(type: "integer", nullable: true),
                    khung_gio_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dich_vu_trong_don", x => x.id);
                    table.CheckConstraint("ck_dich_vu_trong_don_gia_chot_nonnegative", "gia_chot >= 0");
                    table.CheckConstraint("ck_dich_vu_trong_don_nguon", "(dich_vu_id IS NOT NULL) <> (combo_id IS NOT NULL)");
                    table.CheckConstraint("ck_dich_vu_trong_don_size_chot_values", "size_chot IN ('S', 'M', 'L', 'XL')");
                    table.CheckConstraint("ck_dich_vu_trong_don_thoi_luong", "thoi_luong_snapshot > 0");
                    table.CheckConstraint("ck_dich_vu_trong_don_trang_thai_values", "trang_thai IN ('CHO_THUC_HIEN', 'HOAN_TAT', 'HUY')");
                    table.ForeignKey(
                        name: "fk_dich_vu_trong_don_chi_tiet_don_id",
                        column: x => x.chi_tiet_don_id,
                        principalTable: "chi_tiet_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dich_vu_trong_don_combo_id",
                        column: x => x.combo_id,
                        principalTable: "combo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dich_vu_trong_don_dich_vu_id",
                        column: x => x.dich_vu_id,
                        principalTable: "dich_vu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dich_vu_trong_don_khung_gio_id",
                        column: x => x.khung_gio_id,
                        principalTable: "khung_gio_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nhat_ky_cham_soc",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loai_nhat_ky = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ghi_nhan_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    object_path = table.Column<string>(type: "text", nullable: true),
                    ghi_chu = table.Column<string>(type: "text", nullable: true),
                    can_nang_ghi_nhan = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    chi_tiet_don_id = table.Column<int>(type: "integer", nullable: false),
                    nhan_vien_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nhat_ky_cham_soc", x => x.id);
                    table.CheckConstraint("ck_nhat_ky_cham_soc_can_nang_ghi_nhan_positive", "can_nang_ghi_nhan > 0");
                    table.CheckConstraint("ck_nhat_ky_cham_soc_noi_dung", "object_path IS NOT NULL OR ghi_chu IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_nhat_ky_cham_soc_chi_tiet_don_id",
                        column: x => x.chi_tiet_don_id,
                        principalTable: "chi_tiet_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nhat_ky_cham_soc_nhan_vien_id",
                        column: x => x.nhan_vien_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "phu_phi",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_phu_phi = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    so_tien = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    ly_do = table.Column<string>(type: "text", nullable: false),
                    huy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    don_dat_lich_id = table.Column<int>(type: "integer", nullable: false),
                    chi_tiet_don_id = table.Column<int>(type: "integer", nullable: true),
                    tao_boi_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phu_phi", x => x.id);
                    table.CheckConstraint("ck_phu_phi_so_tien", "so_tien > 0");
                    table.CheckConstraint("ck_phu_phi_so_tien_nonnegative", "so_tien >= 0");
                    table.ForeignKey(
                        name: "fk_phu_phi_chi_tiet_don_id",
                        column: x => x.chi_tiet_don_id,
                        principalTable: "chi_tiet_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_phu_phi_don_dat_lich_id",
                        column: x => x.don_dat_lich_id,
                        principalTable: "don_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_phu_phi_tao_boi_id",
                        column: x => x.tao_boi_id,
                        principalTable: "nhan_vien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "danh_gia",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    so_sao = table.Column<int>(type: "integer", nullable: false),
                    binh_luan = table.Column<string>(type: "text", nullable: true),
                    anh_paths = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    don_dat_lich_id = table.Column<int>(type: "integer", nullable: false),
                    khach_hang_id = table.Column<int>(type: "integer", nullable: false),
                    chi_tiet_don_id = table.Column<int>(type: "integer", nullable: true),
                    dich_vu_trong_don_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_danh_gia", x => x.id);
                    table.CheckConstraint("ck_danh_gia_so_sao", "so_sao BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_danh_gia_chi_tiet_don_id",
                        column: x => x.chi_tiet_don_id,
                        principalTable: "chi_tiet_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_danh_gia_dich_vu_trong_don_id",
                        column: x => x.dich_vu_trong_don_id,
                        principalTable: "dich_vu_trong_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_danh_gia_don_dat_lich_id",
                        column: x => x.don_dat_lich_id,
                        principalTable: "don_dat_lich",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_danh_gia_khach_hang_id",
                        column: x => x.khach_hang_id,
                        principalTable: "khach_hang",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "thanh_phan_dich_vu_trong_don",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ten_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    gia_le_size_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    thoi_luong_snapshot = table.Column<int>(type: "integer", nullable: false),
                    trang_thai = table.Column<string>(type: "character varying(40)", nullable: false),
                    hoan_tat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    dich_vu_trong_don_id = table.Column<int>(type: "integer", nullable: false),
                    dich_vu_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thanh_phan_dich_vu_trong_don", x => x.id);
                    table.CheckConstraint("ck_thanh_phan_dich_vu_trong_don_trang_thai_values", "trang_thai IN ('CHO_THUC_HIEN', 'HOAN_TAT', 'HUY')");
                    table.ForeignKey(
                        name: "fk_thanh_phan_dich_vu_trong_don_dich_vu_id",
                        column: x => x.dich_vu_id,
                        principalTable: "dich_vu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_thanh_phan_dich_vu_trong_don_dich_vu_trong_don_id",
                        column: x => x.dich_vu_trong_don_id,
                        principalTable: "dich_vu_trong_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ct_hoa_don",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loai_khoan = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ten_pet_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ten_khoan_muc = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_snapshot = table.Column<string>(type: "character varying(40)", nullable: true),
                    so_luong = table.Column<int>(type: "integer", nullable: false),
                    don_gia_snapshot = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    thanh_tien = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    dien_giai = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    hoa_don_id = table.Column<int>(type: "integer", nullable: false),
                    chi_tiet_don_id = table.Column<int>(type: "integer", nullable: true),
                    dich_vu_trong_don_id = table.Column<int>(type: "integer", nullable: true),
                    phu_phi_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ct_hoa_don", x => x.id);
                    table.CheckConstraint("ck_ct_hoa_don_don_gia_snapshot_nonnegative", "don_gia_snapshot >= 0");
                    table.CheckConstraint("ck_ct_hoa_don_nguon", "(loai_khoan = 'SPA' AND chi_tiet_don_id IS NOT NULL AND dich_vu_trong_don_id IS NOT NULL AND phu_phi_id IS NULL) OR (loai_khoan = 'HOTEL' AND chi_tiet_don_id IS NOT NULL AND dich_vu_trong_don_id IS NULL AND phu_phi_id IS NULL) OR (loai_khoan = 'PHU_PHI' AND phu_phi_id IS NOT NULL AND dich_vu_trong_don_id IS NULL)");
                    table.CheckConstraint("ck_ct_hoa_don_size_snapshot_values", "size_snapshot IN ('S', 'M', 'L', 'XL')");
                    table.CheckConstraint("ck_ct_hoa_don_so_luong", "so_luong > 0");
                    table.CheckConstraint("ck_ct_hoa_don_thanh_tien", "thanh_tien = so_luong * don_gia_snapshot");
                    table.CheckConstraint("ck_ct_hoa_don_thanh_tien_nonnegative", "thanh_tien >= 0");
                    table.ForeignKey(
                        name: "fk_ct_hoa_don_chi_tiet_don_id",
                        column: x => x.chi_tiet_don_id,
                        principalTable: "chi_tiet_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ct_hoa_don_dich_vu_trong_don_id",
                        column: x => x.dich_vu_trong_don_id,
                        principalTable: "dich_vu_trong_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ct_hoa_don_hoa_don_id",
                        column: x => x.hoa_don_id,
                        principalTable: "hoa_don",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ct_hoa_don_phu_phi_id",
                        column: x => x.phu_phi_id,
                        principalTable: "phu_phi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id_hanh_dong_request_id",
                table: "audit_log",
                columns: new[] { "actor_id", "hanh_dong", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_anonymous_request",
                table: "audit_log",
                columns: new[] { "hanh_dong", "request_id" },
                unique: true,
                filter: "actor_id IS NULL AND request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bang_gia_size_combo_id_size_id",
                table: "bang_gia_size",
                columns: new[] { "combo_id", "size_id" },
                unique: true,
                filter: "combo_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bang_gia_size_dich_vu_id_size_id",
                table: "bang_gia_size",
                columns: new[] { "dich_vu_id", "size_id" },
                unique: true,
                filter: "dich_vu_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bang_gia_size_size_id",
                table: "bang_gia_size",
                column: "size_id");

            migrationBuilder.CreateIndex(
                name: "ix_bao_ve_tracking_khoa_ip_hash_khoa_sdt_hash",
                table: "bao_ve_tracking",
                columns: new[] { "khoa_ip_hash", "khoa_sdt_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bao_ve_tracking_mo_boi_id",
                table: "bao_ve_tracking",
                column: "mo_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_cau_hinh_he_thong_khoa",
                table: "cau_hinh_he_thong",
                column: "khoa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cau_hinh_he_thong_sua_boi_id",
                table: "cau_hinh_he_thong",
                column: "sua_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_combo_dich_vu_id",
                table: "chi_tiet_combo",
                column: "dich_vu_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_ban_giao_boi_id",
                table: "chi_tiet_don",
                column: "ban_giao_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_check_in_boi_id",
                table: "chi_tiet_don",
                column: "check_in_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_chi_tiet_don_don_dat_lich_id_thu_cung_id",
                table: "chi_tiet_don",
                columns: new[] { "don_dat_lich_id", "thu_cung_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_ho_so_tiem_chung_id",
                table: "chi_tiet_don",
                column: "ho_so_tiem_chung_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_huy_boi_id",
                table: "chi_tiet_don",
                column: "huy_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_loai_phong_id",
                table: "chi_tiet_don",
                column: "loai_phong_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_nhan_som_boi_id",
                table: "chi_tiet_don",
                column: "nhan_som_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_chi_tiet_don_nhan_vien_id_trang_thai_cham_soc",
                table: "chi_tiet_don",
                columns: new[] { "nhan_vien_id", "trang_thai_cham_soc" });

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_thu_cung_id",
                table: "chi_tiet_don",
                column: "thu_cung_id");

            migrationBuilder.CreateIndex(
                name: "IX_chi_tiet_don_xac_minh_tiem_boi_id",
                table: "chi_tiet_don",
                column: "xac_minh_tiem_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_ct_hoa_don_chi_tiet_don_id",
                table: "ct_hoa_don",
                column: "chi_tiet_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_ct_hoa_don_dich_vu_trong_don_id",
                table: "ct_hoa_don",
                column: "dich_vu_trong_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_ct_hoa_don_hoa_don_id",
                table: "ct_hoa_don",
                column: "hoa_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_ct_hoa_don_phu_phi_id",
                table: "ct_hoa_don",
                column: "phu_phi_id");

            migrationBuilder.CreateIndex(
                name: "IX_dang_ky_ca_ca_lam_viec_id",
                table: "dang_ky_ca",
                column: "ca_lam_viec_id");

            migrationBuilder.CreateIndex(
                name: "IX_dang_ky_ca_dot_id",
                table: "dang_ky_ca",
                column: "dot_id");

            migrationBuilder.CreateIndex(
                name: "IX_dang_ky_ca_duyet_boi_id",
                table: "dang_ky_ca",
                column: "duyet_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_dang_ky_ca_ngay_trang_thai",
                table: "dang_ky_ca",
                columns: new[] { "ngay", "trang_thai" });

            migrationBuilder.CreateIndex(
                name: "ix_dang_ky_ca_nhan_vien_id_ngay_ca_lam_viec_id",
                table: "dang_ky_ca",
                columns: new[] { "nhan_vien_id", "ngay", "ca_lam_viec_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_danh_gia_chi_tiet_don_id",
                table: "danh_gia",
                column: "chi_tiet_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_danh_gia_dich_vu_trong_don_id",
                table: "danh_gia",
                column: "dich_vu_trong_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_danh_gia_don_dat_lich_id",
                table: "danh_gia",
                column: "don_dat_lich_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_danh_gia_khach_hang_id",
                table: "danh_gia",
                column: "khach_hang_id");

            migrationBuilder.CreateIndex(
                name: "IX_dich_vu_trong_don_chi_tiet_don_id",
                table: "dich_vu_trong_don",
                column: "chi_tiet_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_dich_vu_trong_don_combo_id",
                table: "dich_vu_trong_don",
                column: "combo_id");

            migrationBuilder.CreateIndex(
                name: "IX_dich_vu_trong_don_dich_vu_id",
                table: "dich_vu_trong_don",
                column: "dich_vu_id");

            migrationBuilder.CreateIndex(
                name: "ix_dich_vu_trong_don_khung_gio_id_trang_thai",
                table: "dich_vu_trong_don",
                columns: new[] { "khung_gio_id", "trang_thai" });

            migrationBuilder.CreateIndex(
                name: "ix_dich_vu_trong_don_request_id",
                table: "dich_vu_trong_don",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_don_dat_lich_duyet_boi_id",
                table: "don_dat_lich",
                column: "duyet_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_don_dat_lich_huy_boi_id",
                table: "don_dat_lich",
                column: "huy_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_don_dat_lich_khach_hang_id",
                table: "don_dat_lich",
                column: "khach_hang_id");

            migrationBuilder.CreateIndex(
                name: "IX_don_dat_lich_khung_gio_id",
                table: "don_dat_lich",
                column: "khung_gio_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_dat_lich_request_id",
                table: "don_dat_lich",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_don_dat_lich_tao_boi_id",
                table: "don_dat_lich",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_don_dat_lich_tracking_code",
                table: "don_dat_lich",
                column: "tracking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_don_dat_lich_trang_thai_khung_gio_id",
                table: "don_dat_lich",
                columns: new[] { "trang_thai", "khung_gio_id" });

            migrationBuilder.CreateIndex(
                name: "IX_dot_dang_ky_ca_tao_boi_id",
                table: "dot_dang_ky_ca",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_giao_dich_thu_hoa_don_id",
                table: "giao_dich_thu",
                column: "hoa_don_id");

            migrationBuilder.CreateIndex(
                name: "ix_giao_dich_thu_ngan_hang_ma_giao_dich",
                table: "giao_dich_thu",
                columns: new[] { "ngan_hang", "ma_giao_dich" },
                unique: true,
                filter: "ngan_hang IS NOT NULL AND ma_giao_dich IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_giao_dich_thu_request_id",
                table: "giao_dich_thu",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_giao_dich_thu_tien_coc_id",
                table: "giao_dich_thu",
                column: "tien_coc_id");

            migrationBuilder.CreateIndex(
                name: "IX_giao_dich_thu_xac_nhan_boi_id",
                table: "giao_dich_thu",
                column: "xac_nhan_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_he_so_can_nang_ma_size",
                table: "he_so_can_nang",
                column: "ma_size",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ho_so_tiem_chung_thu_cung_id",
                table: "ho_so_tiem_chung",
                column: "thu_cung_id");

            migrationBuilder.CreateIndex(
                name: "ix_hoa_don_don_dat_lich_id",
                table: "hoa_don",
                column: "don_dat_lich_id",
                unique: true,
                filter: "trang_thai <> 'REVOKED'");

            migrationBuilder.CreateIndex(
                name: "ix_hoa_don_ma_hoa_don",
                table: "hoa_don",
                column: "ma_hoa_don",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hoa_don_paid_at",
                table: "hoa_don",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "IX_hoa_don_tao_boi_id",
                table: "hoa_don",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_hoa_don_thay_the_hoa_don_id",
                table: "hoa_don",
                column: "thay_the_hoa_don_id",
                unique: true,
                filter: "thay_the_hoa_don_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_hoa_don_thu_boi_id",
                table: "hoa_don",
                column: "thu_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_hoa_don_thu_hoi_boi_id",
                table: "hoa_don",
                column: "thu_hoi_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_hoan_coc_hoan_boi_id",
                table: "hoan_coc",
                column: "hoan_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_hoan_coc_request_id",
                table: "hoan_coc",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hoan_coc_tao_boi_id",
                table: "hoan_coc",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_hoan_coc_tien_coc_id",
                table: "hoan_coc",
                column: "tien_coc_id");

            migrationBuilder.CreateIndex(
                name: "ix_khach_hang_so_dien_thoai",
                table: "khach_hang",
                column: "so_dien_thoai");

            migrationBuilder.CreateIndex(
                name: "IX_khung_gio_dat_lich_ca_lam_viec_id",
                table: "khung_gio_dat_lich",
                column: "ca_lam_viec_id");

            migrationBuilder.CreateIndex(
                name: "ix_khung_gio_dat_lich_ngay_ca_lam_viec_id",
                table: "khung_gio_dat_lich",
                columns: new[] { "ngay", "ca_lam_viec_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ngay_nghi_ngay",
                table: "ngay_nghi",
                column: "ngay",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ngay_nghi_tao_boi_id",
                table: "ngay_nghi",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "ix_nhan_vien_ten_dang_nhap",
                table: "nhan_vien",
                column: "ten_dang_nhap",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nhan_vien_quyen_cap_boi_id",
                table: "nhan_vien_quyen",
                column: "cap_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_nhan_vien_quyen_ma_quyen",
                table: "nhan_vien_quyen",
                column: "ma_quyen");

            migrationBuilder.CreateIndex(
                name: "ix_nhat_ky_cham_soc_chi_tiet_don_id_ghi_nhan_at",
                table: "nhat_ky_cham_soc",
                columns: new[] { "chi_tiet_don_id", "ghi_nhan_at" });

            migrationBuilder.CreateIndex(
                name: "IX_nhat_ky_cham_soc_nhan_vien_id",
                table: "nhat_ky_cham_soc",
                column: "nhan_vien_id");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenge_khach_hang_id",
                table: "otp_challenge",
                column: "khach_hang_id");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenge_request_id",
                table: "otp_challenge",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_phu_phi_chi_tiet_don_id",
                table: "phu_phi",
                column: "chi_tiet_don_id");

            migrationBuilder.CreateIndex(
                name: "IX_phu_phi_don_dat_lich_id",
                table: "phu_phi",
                column: "don_dat_lich_id");

            migrationBuilder.CreateIndex(
                name: "IX_phu_phi_tao_boi_id",
                table: "phu_phi",
                column: "tao_boi_id");

            migrationBuilder.CreateIndex(
                name: "IX_thanh_phan_dich_vu_trong_don_dich_vu_id",
                table: "thanh_phan_dich_vu_trong_don",
                column: "dich_vu_id");

            migrationBuilder.CreateIndex(
                name: "ix_thanh_phan_dich_vu_trong_don_dich_vu_trong_don_id_dich_vu_id",
                table: "thanh_phan_dich_vu_trong_don",
                columns: new[] { "dich_vu_trong_don_id", "dich_vu_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thu_cung_khach_hang_id",
                table: "thu_cung",
                column: "khach_hang_id");

            migrationBuilder.CreateIndex(
                name: "ix_tien_coc_don_dat_lich_id",
                table: "tien_coc",
                column: "don_dat_lich_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tien_coc_trang_thai_yeu_cau_het_han_at",
                table: "tien_coc",
                columns: new[] { "trang_thai_yeu_cau", "het_han_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "bang_gia_size");

            migrationBuilder.DropTable(
                name: "bao_ve_tracking");

            migrationBuilder.DropTable(
                name: "cau_hinh_he_thong");

            migrationBuilder.DropTable(
                name: "chi_tiet_combo");

            migrationBuilder.DropTable(
                name: "ct_hoa_don");

            migrationBuilder.DropTable(
                name: "dang_ky_ca");

            migrationBuilder.DropTable(
                name: "danh_gia");

            migrationBuilder.DropTable(
                name: "giao_dich_thu");

            migrationBuilder.DropTable(
                name: "hoan_coc");

            migrationBuilder.DropTable(
                name: "ngay_nghi");

            migrationBuilder.DropTable(
                name: "nhan_vien_quyen");

            migrationBuilder.DropTable(
                name: "nhat_ky_cham_soc");

            migrationBuilder.DropTable(
                name: "otp_challenge");

            migrationBuilder.DropTable(
                name: "thanh_phan_dich_vu_trong_don");

            migrationBuilder.DropTable(
                name: "he_so_can_nang");

            migrationBuilder.DropTable(
                name: "phu_phi");

            migrationBuilder.DropTable(
                name: "dot_dang_ky_ca");

            migrationBuilder.DropTable(
                name: "hoa_don");

            migrationBuilder.DropTable(
                name: "tien_coc");

            migrationBuilder.DropTable(
                name: "quyen");

            migrationBuilder.DropTable(
                name: "dich_vu_trong_don");

            migrationBuilder.DropTable(
                name: "chi_tiet_don");

            migrationBuilder.DropTable(
                name: "combo");

            migrationBuilder.DropTable(
                name: "dich_vu");

            migrationBuilder.DropTable(
                name: "don_dat_lich");

            migrationBuilder.DropTable(
                name: "ho_so_tiem_chung");

            migrationBuilder.DropTable(
                name: "loai_phong");

            migrationBuilder.DropTable(
                name: "nhan_vien");

            migrationBuilder.DropTable(
                name: "khung_gio_dat_lich");

            migrationBuilder.DropTable(
                name: "thu_cung");

            migrationBuilder.DropTable(
                name: "ca_lam_viec");

            migrationBuilder.DropTable(
                name: "khach_hang");
        }
    }
}
