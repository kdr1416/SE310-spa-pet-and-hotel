/**
 * @file JSDoc & runtime helpers for .NET Web API Envelope and Pet Spa & Hotel domain
 */

/**
 * Tạo chuẩn ApiResponse Envelope của .NET Web API
 * @template T
 * @param {boolean} success
 * @param {T} data
 * @param {string} [message=""]
 * @param {string[]} [errors=[]]
 * @returns {import('./api').ApiResponse<T>}
 */
export function createApiResponse(success, data, message = "", errors = []) {
  return {
    success,
    data,
    message,
    errors,
  };
}

/**
 * Domain Enum Constants
 */
export const LoaiPet = Object.freeze({
  CHO: "CHO",
  MEO: "MEO",
});

export const LoaiDon = Object.freeze({
  SPA: "SPA",
  HOTEL: "HOTEL",
  COMBO_SPA_HOTEL: "COMBO_SPA_HOTEL",
});

export const TrangThaiDon = Object.freeze({
  PENDING: "PENDING",
  CONFIRMED: "CONFIRMED",
  IN_PROGRESS: "IN_PROGRESS",
  READY: "READY",
  COMPLETED: "COMPLETED",
  CANCELLED: "CANCELLED",
});

export const TrangThaiChamSoc = Object.freeze({
  WAITING: "WAITING",
  IN_PROGRESS: "IN_PROGRESS",
  PAUSED: "PAUSED",
  READY: "READY",
  DONE: "DONE",
});

export const TrangThaiThanhToan = Object.freeze({
  UNPAID: "UNPAID",
  PARTIAL_PAID: "PARTIAL_PAID",
  PAID: "PAID",
  REFUNDED: "REFUNDED",
});

export const PhuongThucThanhToan = Object.freeze({
  CASH: "CASH",
  BANK_TRANSFER: "BANK_TRANSFER",
});

export const UserRole = Object.freeze({
  ADMIN: "ADMIN",
  STAFF: "STAFF",
});

export const TrangThaiDuyetCa = Object.freeze({
  PENDING: "PENDING",
  APPROVED: "APPROVED",
  REJECTED: "REJECTED",
});
