import { nights } from "./dates";
export function weightFactor(w) {
  if (!Number.isFinite(Number(w)) || Number(w) <= 0 || Number(w) > 150)
    throw new Error("Cân nặng phải từ 0.1 đến 150kg.");
  return w < 5 ? 1 : w < 10 ? 1.3 : w < 20 ? 1.6 : 2;
}
export function estimatePet(pet, date, catalog) {
  const service = catalog.services.find((s) => s.id === Number(pet.serviceId));
  const combo = catalog.combos.find((s) => s.id === Number(pet.comboId));
  const room = catalog.rooms.find((s) => s.id === Number(pet.roomId));
  const hasSpa = pet.type !== "HOTEL",
    hasHotel = pet.type !== "SPA";
  if (hasSpa && ((!combo && !service) || (combo || service).isActive === false))
    throw new Error("Chọn dịch vụ đang hoạt động.");
  if (hasSpa && !combo && service.loaiDichVu === "SPA_ADVANCED")
    throw new Error("Cắt tỉa phải chọn trong combo có tắm.");
  if (
    hasHotel &&
    (!room ||
      room.isActive === false ||
      !Number.isInteger(nights(date, pet.checkout)) ||
      nights(date, pet.checkout) <= 0)
  )
    throw new Error("Hotel cần phòng và ngày trả sau ngày nhận.");
  const spa = hasSpa
    ? Math.round(
        (combo?.giaCombo ?? service.giaCoBan) *
          weightFactor(Number(pet.weight)),
      )
    : 0;
  const hotel = hasHotel ? room.giaTheoNgay * nights(date, pet.checkout) : 0;
  return {
    amount: spa + hotel,
    spa,
    hotel,
    label: [
      hasSpa ? combo?.tenCombo || service.tenDichVu : "",
      hasHotel ? `${room.tenPhong} · ${nights(date, pet.checkout)} đêm` : "",
    ]
      .filter(Boolean)
      .join(" + "),
  };
}
export function estimateBooking(payload, catalog) {
  const items = payload.pets.map((p) => estimatePet(p, payload.date, catalog));
  return { items, total: items.reduce((sum, item) => sum + item.amount, 0) };
}
