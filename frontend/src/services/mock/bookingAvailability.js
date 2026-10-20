import { addDays, dateNumber } from "../../utils/dates";

export const OPEN_STATUSES = ["PENDING", "CONFIRMED", "IN_PROGRESS", "READY"];
export const hasSpa = (pet) => pet.type !== "HOTEL";
export const hasHotel = (pet) => pet.type !== "SPA";

/** Additive migration only: do not replace bookings, photos or existing configuration. */
export function upgradeMockState(s, now = Date.now()) {
  s.settings ||= {};
  s.settings.vaccinePolicy = { SPA: false, HOTEL: false, COMBO_SPA_HOTEL: false, ...s.settings.vaccinePolicy };
  s.closedDays ||= {};
  s.mailOutbox ||= [];
  s.resendAttempts ||= {};
  for (const room of s.catalog.rooms) room.soLuongPhong ??= 3;
  for (const period of s.periods || []) {
    if (!period.openedAt && ((period.open && now >= Date.parse(period.openAt)) || period.published)) period.openedAt = period.openAt;
  }
  s.featureVersion = 2;
  return s;
}

export function stayDays(start, end) {
  const length = (dateNumber(end) - dateNumber(start)) / 86400000;
  if (!Number.isInteger(length) || length < 1 || length > 366)
    throw new Error("Khoảng lưu trú cần từ 1 đến 366 đêm.");
  return Array.from({ length }, (_, i) => addDays(start, i));
}

export function roomRemaining(s, roomId, date) {
  const room = s.catalog.rooms.find((r) => r.id === Number(roomId));
  const total = room?.soLuongPhong ?? 3;
  let reserved = 0;
  for (const b of s.bookings.filter((b) => OPEN_STATUSES.includes(b.trangThai))) {
    reserved += b.chiTietDonList.filter((d) => !d.laDichVuThem &&
      hasHotel(d.input) && Number(d.input.roomId) === Number(roomId) &&
      b.date <= date && date < d.input.checkout).length;
  }
  return { total, reserved, left: total - reserved };
}

export function assertBookingAvailability(s, payload) {
  if (s.closedDays[payload.date]) throw new Error("Ngày nhận đã nghỉ tiếp nhận. Vui lòng chọn ngày khác.");
  const pets = payload.pets || [];
  const hotelPets = pets.filter(hasHotel);
  if (!hotelPets.length) return;
  if (hotelPets.length !== pets.length)
    throw new Error("Đơn qua đêm cần phòng cho mọi pet. Hãy tách đơn Spa đón trong ngày.");
  const end = hotelPets[0].checkout;
  if (hotelPets.some((p) => p.checkout !== end)) throw new Error("Mọi pet cần cùng ngày trả dự kiến.");
  if (s.closedDays[end]) throw new Error("Ngày trả đã nghỉ nhận–trả Hotel. Vui lòng chọn ngày khác.");
  const counts = new Map();
  for (const pet of hotelPets) counts.set(Number(pet.roomId), (counts.get(Number(pet.roomId)) || 0) + 1);
  for (const day of stayDays(payload.date, end)) {
    for (const [roomId, count] of counts) {
      if (roomRemaining(s, roomId, day).left < count)
        throw new Error(`Loại phòng đã hết suất vào ${day}. Vui lòng chọn phòng/ngày khác.`);
    }
  }
}

export function roomCalendar(s, start, count = 14) {
  return Array.from({ length: Math.min(31, Math.max(1, Number(count) || 14)) }, (_, i) => {
    const date = addDays(start, i);
    return { date, closed: !!s.closedDays[date], rooms: s.catalog.rooms.filter((r) => r.isActive !== false)
      .map((r) => ({ id: r.id, ...roomRemaining(s, r.id, date) })) };
  });
}
