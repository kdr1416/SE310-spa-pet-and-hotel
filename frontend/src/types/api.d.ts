/**
 * Type definitions matching .NET 8/9 Web API Envelope & SRS Pet Spa & Hotel Database Schema
 */

// ==========================================
// 1. .NET 8/9 Web API Response Envelope
// ==========================================
export interface ApiResponse<T = any> {
  success: boolean;
  data: T;
  message: string;
  errors: string[];
}

export interface ApiPaginationResponse<T = any> {
  items: T[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

// ==========================================
// 2. Authentication & User State
// ==========================================
export type UserRole = "ADMIN" | "STAFF";

export interface AuthUser {
  id: number;
  tenNhanVien: string;
  tenDangNhap: string;
  vaiTro: UserRole;
  /** Temporary mock work group; final Backend authorization model remains undecided. */
  workGroup?: "RECEPTION" | "SPA";
  soDienThoai?: string;
  email?: string;
  isActive: boolean;
}

export interface LoginRequest {
  tenDangNhap: string;
  matKhau: string;
}

export interface LoginResponseData {
  token: string;
  expiresIn: number;
  user: AuthUser;
}

export interface AuthState {
  token: string | null;
  user: AuthUser | null;
  isAuthenticated: boolean;
  loading: boolean;
}

// ==========================================
// 3. SRS Domain Enums & Entities
// ==========================================
export type LoaiPet = "CHO" | "MEO";
export type LoaiDon = "SPA" | "HOTEL" | "COMBO_SPA_HOTEL";
export type TrangThaiDon =
  "PENDING" | "CONFIRMED" | "IN_PROGRESS" | "READY" | "COMPLETED" | "CANCELLED";
export type TrangThaiChamSoc =
  "WAITING" | "IN_PROGRESS" | "PAUSED" | "READY" | "DONE";
export type TrangThaiThanhToan =
  "UNPAID" | "PARTIAL_PAID" | "PAID" | "REFUNDED";
export type PhuongThucThanhToan = "CASH" | "BANK_TRANSFER";
export type TrangThaiDuyetCa = "PENDING" | "APPROVED" | "REJECTED";

export interface KhachHang {
  id: number;
  soDienThoai: string;
  email: string;
  tenKhachHang: string;
  thongTinStk?: string;
  createdAt: string;
  updatedAt: string;
}

export interface ThuCung {
  id: number;
  khachHangId: number;
  tenThuCung: string;
  loaiPet: LoaiPet;
  giongLoai?: string;
  canNangDangKy?: number;
  ghiChuDiUng?: string;
  hoSoTiemChung?: HoSoTiemChung[];
}

export interface HoSoTiemChung {
  id: number;
  thuCungId: number;
  duongDanAnh: string;
  ngayTiem?: string;
  loaiVacXin?: string;
}

export interface DichVu {
  id: number;
  tenDichVu: string;
  loaiDichVu: "SPA_BASIC" | "SPA_ADVANCED" | "HEALTHCARE";
  moTa?: string;
  giaCoBan: number;
  thoiLuongPhut: number;
  isActive: boolean;
}

export interface Combo {
  id: number;
  tenCombo: string;
  moTa?: string;
  giaCombo: number;
  isActive: boolean;
  danhSachDichVu?: DichVu[];
}

export interface HeSoCanNang {
  id: number;
  canNangTu: number;
  canNangDen: number;
  heSo: number;
  ghiChu?: string;
}

export interface LoaiPhong {
  id: number;
  tenPhong: string;
  kichCo: "S" | "M" | "L" | "XL";
  giaTheoNgay: number;
  isActive: boolean;
}

export interface CaLamViec {
  id: number;
  tenCa: string;
  gioBatDau: string; // HH:mm:ss
  gioKetThuc: string; // HH:mm:ss
  slotPetMacDinh: number;
  isActive: boolean;
}

export interface KhungGioDatLich {
  id: number;
  ngay: string; // YYYY-MM-DD
  caLamViecId: number;
  caLamViec?: CaLamViec;
  tongSlot: number;
  slotDaDat: number;
  slotConLai: number;
}

export interface ChiTietDon {
  id: number;
  donDatLichId: number;
  thuCungId: number;
  thuCung?: ThuCung;
  dichVuId?: number;
  dichVu?: DichVu;
  comboId?: number;
  combo?: Combo;
  loaiPhongId?: number;
  loaiPhong?: LoaiPhong;
  nhanVienId?: number;
  nhanVien?: AuthUser;
  canNangThucTe?: number;
  tinhTrangLong?: string;
  donGiaChot: number;
  laDichVuThem: boolean;
  trangThaiChamSoc: TrangThaiChamSoc;
  nhatKyChamSoc?: NhatKyChamSoc[];
  phuPhiList?: PhuPhi[];
}

export interface NhatKyChamSoc {
  id: number;
  chiTietDonId: number;
  nhanVienId: number;
  loaiNhatKy: "CHECK_IN" | "IN_PROGRESS" | "SU_CO" | "READY_CHECK_OUT";
  duongDanAnh: string;
  ghiChuSucKhoe: string;
  thoiGianGhiNhan: string;
}

export interface DonDatLich {
  id: number;
  trackingCode: string;
  khachHangId: number;
  khachHang?: KhachHang;
  khungGioId?: number;
  khungGio?: KhungGioDatLich;
  loaiDon: LoaiDon;
  trangThai: TrangThaiDon;
  thoiGianBatDau: string;
  thoiGianKetThuc?: string;
  tongTienTamTinh: number;
  ghiChuKhach?: string;
  chiTietDonList: ChiTietDon[];
  hoaDon?: HoaDon;
  createdAt: string;
  updatedAt: string;
}

export interface HoaDon {
  id: number;
  donDatLichId: number;
  tongTienDichVu: number;
  tongPhuPhi: number;
  giamGia: number;
  tongThanhToan: number;
  tienCoc: number;
  daThanhToan: number;
  trangThaiThanhToan: TrangThaiThanhToan;
  phuongThucThanhToan?: PhuongThucThanhToan;
  ctHoaDonList?: CtHoaDon[];
  phuPhiList?: PhuPhi[];
  createdAt: string;
  updatedAt: string;
}

export interface CtHoaDon {
  id: number;
  hoaDonId: number;
  chiTietDonId?: number;
  tenKhoanMuc: string;
  soLuong: number;
  donGiaSnapshot: number;
  thanhTien: number;
}

export interface PhuPhi {
  id: number;
  hoaDonId: number;
  chiTietDonId?: number;
  tenPhuPhi: string;
  soTien: number;
  lyDo?: string;
  createdAt: string;
}

export interface DangKyCa {
  id: number;
  nhanVienId: number;
  caLamViecId: number;
  caLamViec?: CaLamViec;
  ngayLamViec: string;
  trangThaiDuyet: TrangThaiDuyetCa;
  lyDoXinNghiHoacDoi?: string;
  createdAt: string;
}
