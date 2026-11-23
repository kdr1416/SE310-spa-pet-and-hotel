import httpClient from "./httpClient";

const routes = new Map([
  ["getBookingOptions", "/booking-options"],
  ["getAssignmentCandidates", "/internal/assignment-candidates"],
  ["getFurConditions", "/internal/fur-conditions"],
  ["listCareTasks", "/internal/care-tasks"],
]);

export const supportsPhase2Action = (action) => routes.has(action);

export function callPhase2Operation(action, payload = {}) {
  if (action === "getAssignmentCandidates" && (!Number.isInteger(payload.maCa) || payload.maCa <= 0)) {
    throw { success: false, data: null, message: "Chọn ca thao tác hiện tại.", errorCode: "VALIDATION_FAILED",
      errors: [{ field: "maCa", message: "Cần mã ca hợp lệ." }] };
  }
  const keys = action === "getBookingOptions" ? ["ngay"]
    : action === "getAssignmentCandidates" ? ["ngay", "maCa"]
    : action === "listCareTasks" ? ["trang", "kichThuocTrang", "tuNgay", "denNgay", "trangThai"] : [];
  const params = Object.fromEntries(keys.filter(key => payload[key] !== undefined).map(key => [key, payload[key]]));
  return httpClient.get(routes.get(action), { params });
}
