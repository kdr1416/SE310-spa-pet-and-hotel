/**
 * @file formatters.js
 * Tiện ích định dạng tiền tệ, ngày giờ và mapping trạng thái chuẩn SRS
 */

/**
 * Định dạng tiền tệ VNĐ (ví dụ: 150.000 ₫)
 * @param {number|string} amount
 * @returns {string}
 */
export function formatCurrency(amount) {
  if (amount === undefined || amount === null || isNaN(amount)) return "0 ₫";
  return new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0,
  }).format(Number(amount));
}

/**
 * Định dạng ngày giờ VN (ví dụ: 08:30 - 08/10/2026)
 * @param {string|Date} dateStr
 * @returns {string}
 */
export function formatDateTime(dateStr) {
  if (!dateStr) return "--:--";
  const date = new Date(dateStr);
  return new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    timeZone: "Asia/Ho_Chi_Minh",
  }).format(date);
}

/**
 * Định dạng ngày (ví dụ: 08/10/2026)
 * @param {string|Date} dateStr
 * @returns {string}
 */
export function formatDate(dateStr) {
  if (!dateStr) return "--/--/----";
  const date = new Date(dateStr);
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

/**
 * Định dạng giờ (ví dụ: 08:30)
 * @param {string} timeStr
 * @returns {string}
 */
export function formatTime(timeStr) {
  if (!timeStr) return "--:--";
  if (timeStr.includes(":")) {
    const parts = timeStr.split(":");
    return `${parts[0]}:${parts[1]}`;
  }
  return timeStr;
}

/**
 * Trả về cấu hình nhãn, màu nền, màu chữ cho trạng thái chăm sóc thú cưng
 * @param {string} status 'WAITING' | 'IN_PROGRESS' | 'PAUSED' | 'READY' | 'DONE'
 */
export function getCareStatusBadge(status) {
  switch (status) {
    case "WAITING":
      return {
        label: "Chờ chăm sóc",
        colorClass: "bg-amber-50 text-amber-700 border-amber-200",
        dotClass: "bg-amber-500",
      };
    case "IN_PROGRESS":
      return {
        label: "Đang chăm sóc",
        colorClass: "bg-blue-50 text-blue-700 border-blue-200",
        dotClass: "bg-blue-500 animate-pulse",
      };
    case "PAUSED":
      return {
        label: "Tạm dừng (Phát sinh)",
        colorClass: "bg-orange-50 text-orange-700 border-orange-200",
        dotClass: "bg-orange-500",
      };
    case "READY":
      return {
        label: "Sẵn sàng đón bé",
        colorClass: "bg-emerald-50 text-emerald-700 border-emerald-200",
        dotClass: "bg-emerald-500",
      };
    case "DONE":
      return {
        label: "Hoàn tất dịch vụ",
        colorClass: "bg-teal-50 text-teal-700 border-teal-200",
        dotClass: "bg-teal-500",
      };
    default:
      return {
        label: status || "Không rõ",
        colorClass: "bg-slate-50 text-slate-700 border-slate-200",
        dotClass: "bg-slate-400",
      };
  }
}

/**
 * Trả về cấu hình trạng thái tổng quát của Đơn đặt lịch (Booking)
 * @param {string} status 'PENDING' | 'CONFIRMED' | 'IN_PROGRESS' | 'READY' | 'COMPLETED' | 'CANCELLED'
 */
export function getBookingStatusBadge(status) {
  switch (status) {
    case "PENDING":
      return {
        label: "Chờ duyệt",
        colorClass: "bg-amber-50 text-amber-700 border-amber-200",
      };
    case "CONFIRMED":
      return {
        label: "Đã xác nhận",
        colorClass: "bg-sky-50 text-sky-700 border-sky-200",
      };
    case "IN_PROGRESS":
      return {
        label: "Đang thực hiện",
        colorClass: "bg-blue-50 text-blue-700 border-blue-200",
      };
    case "READY":
      return {
        label: "Chờ nhận pet",
        colorClass: "bg-emerald-50 text-emerald-700 border-emerald-200",
      };
    case "COMPLETED":
      return {
        label: "Hoàn thành",
        colorClass: "bg-teal-50 text-teal-700 border-teal-200",
      };
    case "CANCELLED":
      return {
        label: "Đã hủy",
        colorClass: "bg-rose-50 text-rose-700 border-rose-200",
      };
    default:
      return {
        label: status || "N/A",
        colorClass: "bg-slate-50 text-slate-700 border-slate-200",
      };
  }
}
