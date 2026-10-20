import { dateNumber, localDate } from "./dates";
import { estimatePet } from "./pricing";
export const normalizePhone = (s) => String(s || "").replace(/\D/g, "");
export function validateImage(file) {
  if (
    !(file instanceof Blob) ||
    !["image/jpeg", "image/png", "image/webp"].includes(file.type) ||
    file.size <= 0 ||
    file.size > 5 * 1024 * 1024
  )
    throw new Error("Ảnh phải là JPEG/PNG/WebP, tối đa 5MB.");
}
export function validateBooking(payload, available, catalog) {
  const errors = [],
    c = payload.customer || {};
  if (!c.name?.trim() || c.name.length > 100)
    errors.push("Nhập tên khách hàng (tối đa 100 ký tự).");
  if (!/^0\d{9}$/.test(normalizePhone(c.phone)))
    errors.push("Số điện thoại cần 10 chữ số, bắt đầu bằng 0.");
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(c.email || ""))
    errors.push("Nhập email hợp lệ.");
  if (!Number.isFinite(dateNumber(payload.date)) || payload.date < localDate())
    errors.push("Ngày đặt lịch không được ở quá khứ.");
  if (!payload.pets?.length) errors.push("Cần ít nhất một thú cưng.");
  if (payload.pets?.filter((p) => p.type !== "HOTEL").length > available)
    errors.push(`Khung giờ chỉ còn ${available} chỗ trống.`);
  for (const pet of payload.pets || []) {
    if (
      !pet.name?.trim() ||
      pet.name.length > 100 ||
      !["CHO", "MEO"].includes(pet.species)
    )
      errors.push("Nhập tên và loài cho mỗi bé.");
    if (
      !["SPA", "HOTEL", "COMBO_SPA_HOTEL"].includes(pet.type) ||
      !Number.isFinite(Number(pet.weight)) ||
      pet.weight < 0.1 ||
      pet.weight > 150
    )
      errors.push("Kiểm tra loại dịch vụ và cân nặng 0.1–150kg.");
    try {
      estimatePet(pet, payload.date, catalog);
    } catch (e) {
      errors.push(e.message);
    }
    if (pet.coatType && !["SHORT", "LONG", "OTHER"].includes(pet.coatType))
      errors.push("Loại lông không hợp lệ.");
    if (pet.vaccine) {
      try {
        validateImage(pet.vaccine);
      } catch {
        errors.push(
          "Ảnh giấy tiêm chủng cần JPEG/PNG/WebP ≤ 5MB.",
        );
      }
    }
  }
  return [...new Set(errors)];
}
export function deriveBookingStatus(details, current) {
  if (["CANCELLED", "COMPLETED", "PENDING"].includes(current)) return current;
  if (
    details.length &&
    details.every((d) => ["READY", "DONE"].includes(d.trangThaiChamSoc))
  )
    return "READY";
  if (
    details.some(
      (d) =>
        d.checkedIn ||
        ["IN_PROGRESS", "PAUSED", "READY"].includes(d.trangThaiChamSoc),
    )
  )
    return "IN_PROGRESS";
  return "CONFIRMED";
}
