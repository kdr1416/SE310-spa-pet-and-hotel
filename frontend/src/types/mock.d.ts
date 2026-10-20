/** Frontend-only DTOs. Do not treat these as confirmed Backend/database contracts. */
import type {
  AuthUser,
  LoaiPet,
  LoaiDon,
  TrangThaiDon,
  TrangThaiChamSoc,
  PhuongThucThanhToan,
} from "./api";
export interface PetBookingInput {
  name: string;
  species: LoaiPet;
  breed?: string;
  coatType?: "SHORT" | "LONG" | "OTHER";
  weight: number;
  type: LoaiDon;
  serviceId?: number | null;
  comboId?: number | null;
  roomId?: number;
  checkout?: string;
  vaccine?: Blob | null;
  notes?: string;
}
export interface CreateBookingRequest {
  requestId: string;
  customer: { name: string; phone: string; email: string };
  date: string;
  shiftId: number;
  pets: PetBookingInput[];
}
export interface BookingLookupRequest {
  trackingCode: string;
  phone: string;
}
export interface MockCareLog {
  id: string;
  nhanVienId: number;
  loaiNhatKy: string;
  photo: Blob | null;
  ghiChuSucKhoe: string;
  thoiGianGhiNhan: string;
}
export interface MockBookingDetail {
  id: string;
  input: PetBookingInput;
  label: string;
  thuCung: {
    tenThuCung: string;
    loaiPet: LoaiPet;
    giongLoai: string;
    canNangDangKy: number;
    ghiChuDiUng: string;
    loaiLong?: "SHORT" | "LONG" | "OTHER" | null;
  };
  donGiaChot: number;
  canNangThucTe?: number;
  tinhTrangLong?: string;
  nhanVienId: number | null;
  checkedIn: boolean;
  vaccineVerification?: { by: number; at: string };
  trangThaiChamSoc: TrangThaiChamSoc;
  nhatKyChamSoc: MockCareLog[];
  laDichVuThem: boolean;
}
export interface MockInvoice {
  id: string;
  tongTienDichVu: number;
  tongPhuPhi: number;
  tongThanhToan: number;
  daThanhToan: number;
  trangThaiThanhToan: "UNPAID" | "PAID";
  phuongThucThanhToan?: PhuongThucThanhToan;
  ctHoaDonList: {
    id: string;
    tenKhoanMuc: string;
    soLuong: number;
    donGiaSnapshot: number;
    thanhTien: number;
  }[];
  phuPhiList: { id: string; name: string; amount: number; note: string }[];
  createdAt: string;
}
export interface MockBooking {
  id: string;
  requestId: string;
  trackingCode: string;
  khachHang: { tenKhachHang: string; soDienThoai: string; email: string };
  date: string;
  shiftId: number;
  trangThai: TrangThaiDon;
  thoiGianBatDau: string;
  tongTienTamTinh: number;
  ghiChuKhach?: string;
  endDate?: string;
  bookingEmail?: string;
  loaiHuy?: "STAFF_REJECTED";
  lyDoHuy?: string;
  huyAt?: string;
  nguoiHuyId?: number;
  chiTietDonList: MockBookingDetail[];
  hoaDon: MockInvoice | null;
  createdAt: string;
  updatedAt: string;
}
export interface MockCustomerRequest {
  id: string;
  bookingId: string;
  trackingCode: string;
  type: "ADDON" | "REVIEW" | "COMPLAINT" | "REFUND";
  note: string;
  rating: number | null;
  detailId?: string;
  serviceId?: number;
  status: "PENDING" | "APPROVED" | "REJECTED";
  amount?: number;
  resolution?: string;
  createdAt: string;
}
export interface MockRegistration {
  id: string;
  periodId: string | number;
  employeeId: number;
  date: string;
  shiftId: number;
  status: "PENDING" | "APPROVED" | "REJECTED";
  purpose?: "RECEPTION" | "HOTEL_CARE";
  createdAt: string;
}
export interface MockPeriod {
  id: string | number;
  week: string;
  openAt: string;
  deadline: string;
  capacity: number;
  open: boolean;
  published: boolean;
  openedAt?: string | null;
}
export type MockVaccinePolicy = Record<LoaiDon, boolean>;
export interface MockMail {
  id: string;
  to: string;
  trackingCodes: string[];
  createdAt: string;
}
export type MockUser = AuthUser & { workGroup: "RECEPTION" | "SPA" };
