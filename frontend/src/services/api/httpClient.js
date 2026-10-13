/**
 * @file httpClient.js
 * Cấu hình Axios instance chuẩn .NET 8/9 Web API Envelope, hỗ trợ Mock Delay,
 * Token injection và Response Interceptors.
 */

import axios from "axios";
import {
  mockBookings,
  mockDichVuList,
  mockComboList,
  mockHeSoCanNang,
  mockLoaiPhong,
  mockCaLamViec,
  mockKhungGioDatLich,
  mockUsers,
} from "./mockData";

// Cờ cấu hình: Ưu tiên gọi Backend .NET nếu VITE_USE_MOCK_API = 'false'
const IS_MOCK_ENABLED = import.meta.env.VITE_USE_MOCK_API !== "false";
const MOCK_LATENCY_MS = 450;

/**
 * Hàm mô phỏng độ trễ mạng (Network Latency)
 * @param {number} ms
 */
export const mockDelay = (ms = MOCK_LATENCY_MS) =>
  new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Chuẩn hóa Envelope phản hồi .NET Web API
 * Envelope: { success: boolean, data: any, message: string, errors: string[] }
 */
export function normalizeEnvelope(
  data,
  message = "Thao tác thành công",
  success = true,
  errors = [],
) {
  return {
    success,
    data,
    message,
    errors,
  };
}

// Khởi tạo Axios Instance
const httpClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || "/api",
  timeout: 12000,
  headers: {
    "Content-Type": "application/json",
    Accept: "application/json",
  },
});

// =========================================================================
// 1. REQUEST INTERCEPTOR: Tự động đính kèm JWT Bearer Token
// =========================================================================
httpClient.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem("petspa_auth_token");
    if (token && !config.headers.Authorization) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error),
);

// =========================================================================
// 2. RESPONSE INTERCEPTOR: Unwrap .NET Envelope & Chuẩn hóa lỗi
// =========================================================================
httpClient.interceptors.response.use(
  (response) => {
    // Nếu backend trả về đúng Envelope .NET { success, data, message, errors }
    const resData = response.data;
    if (resData && typeof resData === "object" && "success" in resData) {
      return resData;
    }
    // Nếu backend trả về dữ liệu trần (raw data), tự bọc vào Envelope
    return normalizeEnvelope(resData);
  },
  (error) => {
    const errorResponse = {
      success: false,
      data: null,
      message: "Có lỗi xảy ra, vui lòng thử lại!",
      errors: [],
    };

    if (error.response) {
      const status = error.response.status;
      const data = error.response.data;

      switch (status) {
        case 401:
          errorResponse.message =
            "Phiên làm việc đã hết hạn hoặc bạn chưa đăng nhập.";
          localStorage.removeItem("petspa_auth_token");
          localStorage.removeItem("petspa_auth_user");
          window.dispatchEvent(new CustomEvent("auth:unauthorized"));
          break;

        case 403:
          errorResponse.message = "Bạn không có quyền thực hiện thao tác này.";
          break;

        case 400:
        case 422:
          // Xử lý ModelState validation errors từ ASP.NET Core
          if (data && data.errors && typeof data.errors === "object") {
            const extracted = [];
            Object.values(data.errors).forEach((errList) => {
              if (Array.isArray(errList)) {
                extracted.push(...errList);
              } else if (typeof errList === "string") {
                extracted.push(errList);
              }
            });
            errorResponse.errors = extracted;
            errorResponse.message =
              data.title || "Dữ liệu gửi lên không hợp lệ.";
          } else {
            errorResponse.message = data?.message || "Yêu cầu không hợp lệ.";
            if (data?.errors) errorResponse.errors = data.errors;
          }
          break;

        case 404:
          errorResponse.message =
            data?.message || "Không tìm thấy dữ liệu yêu cầu.";
          break;

        case 409: // Conflict: Overbooking / Hết slot (BR-03)
          errorResponse.message =
            data?.message || "Khung giờ đã hết chỗ hoặc dữ liệu bị xung đột.";
          break;

        case 500:
        default:
          errorResponse.message =
            data?.message ||
            "Lỗi hệ thống máy chủ .NET. Vui lòng liên hệ Admin.";
          break;
      }
    } else if (error.request) {
      errorResponse.message = "Không thể kết nối đến máy chủ Backend .NET.";
      errorResponse.errors = ["Network Error / Server Offline"];
    } else {
      errorResponse.message = error.message;
    }

    return Promise.reject(errorResponse);
  },
);

// =========================================================================
// 3. MOCK API SERVICE: Sẵn sàng test độc lập cho Frontend
// =========================================================================
export const mockApiService = {
  // --- AUTH ---
  async login(tenDangNhap, matKhau) {
    await mockDelay(500);
    const user = mockUsers.find((u) => u.tenDangNhap === tenDangNhap);
    if (!user || matKhau !== "123456") {
      const err = normalizeEnvelope(
        null,
        "Tên đăng nhập hoặc mật khẩu không chính xác",
        false,
        ["Invalid Credentials"],
      );
      return Promise.reject(err);
    }
    const token = `fake-jwt-token-for-${user.tenDangNhap}-${Date.now()}`;
    return normalizeEnvelope(
      {
        token,
        expiresIn: 3600 * 8,
        user,
      },
      "Đăng nhập thành công",
    );
  },

  // --- BOOKINGS (LỊCH HẸN) ---
  async getBookings(filters = {}) {
    await mockDelay(350);
    let result = [...mockBookings];
    if (filters.status) {
      result = result.filter((b) => b.trangThai === filters.status);
    }
    if (filters.search) {
      const q = filters.search.toLowerCase();
      result = result.filter(
        (b) =>
          b.trackingCode.toLowerCase().includes(q) ||
          b.khachHang?.tenKhachHang?.toLowerCase().includes(q) ||
          b.khachHang?.soDienThoai?.includes(q),
      );
    }
    return normalizeEnvelope(result, "Lấy danh sách lịch hẹn thành công");
  },

  async getBookingByTracking(trackingCode, soDienThoai) {
    await mockDelay(400);
    const booking = mockBookings.find(
      (b) => b.trackingCode.toUpperCase() === trackingCode.toUpperCase(),
    );
    if (!booking) {
      return Promise.reject(
        normalizeEnvelope(
          null,
          "Không tìm thấy đơn đặt lịch với thông tin đã cung cấp",
          false,
          ["Not Found"],
        ),
      );
    }
    // BR-01: Kiểm tra SĐT nếu có truyền
    if (soDienThoai && booking.khachHang?.soDienThoai !== soDienThoai) {
      return Promise.reject(
        normalizeEnvelope(
          null,
          "Số điện thoại không khớp với mã Tracking",
          false,
          ["Invalid Phone Number"],
        ),
      );
    }
    return normalizeEnvelope(booking, "Tra cứu thành công");
  },

  // --- CHECK-IN TẠI QUẦY (UC23, BR-04) ---
  async checkInPet(chiTietDonId, canNangThucTe, tinhTrangLong) {
    await mockDelay(500);
    for (const booking of mockBookings) {
      const item = booking.chiTietDonList.find((c) => c.id === chiTietDonId);
      if (item) {
        item.canNangThucTe = canNangThucTe;
        item.tinhTrangLong = tinhTrangLong;

        // Tính hệ số cân nặng
        const matchedHeSo = mockHeSoCanNang.find(
          (h) => canNangThucTe >= h.canNangTu && canNangThucTe <= h.canNangDen,
        );
        const heSo = matchedHeSo ? matchedHeSo.heSo : 1.0;

        const basePrice = item.combo
          ? item.combo.giaCombo
          : item.dichVu?.giaCoBan || 150000;
        item.donGiaChot = basePrice * heSo;
        item.trangThaiChamSoc = "WAITING";
        booking.trangThai = "IN_PROGRESS";

        return normalizeEnvelope(
          {
            chiTietDon: item,
            donGiaChot: item.donGiaChot,
            heSo,
          },
          "Tiếp nhận thú cưng và chốt giá thành công",
        );
      }
    }
    return Promise.reject(
      normalizeEnvelope(null, "Không tìm thấy chi tiết đơn", false),
    );
  },

  // --- MASTER DATA ---
  async getDichVuList() {
    await mockDelay(250);
    return normalizeEnvelope(mockDichVuList);
  },

  async getComboList() {
    await mockDelay(250);
    return normalizeEnvelope(mockComboList);
  },

  async getKhungGioList(ngay) {
    await mockDelay(300);
    const list = mockKhungGioDatLich.filter((k) => !ngay || k.ngay === ngay);
    return normalizeEnvelope(list);
  },
};

export default httpClient;
export { IS_MOCK_ENABLED };
