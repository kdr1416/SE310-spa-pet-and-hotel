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
  };
  donGiaChot: number;
  canNangThucTe?: number;
  tinhTrangLong?: string;
  nhanVienId: number | null;
  checkedIn: boolean;
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
}
export type MockUser = AuthUser & { workGroup: "RECEPTION" | "SPA" };
