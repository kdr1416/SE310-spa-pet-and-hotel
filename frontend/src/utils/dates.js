export function localDate(date = new Date()) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Ho_Chi_Minh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(date);
}
export function dateNumber(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || "")) return NaN;
  const n = Date.parse(value + "T00:00:00Z");
  return Number.isFinite(n) && new Date(n).toISOString().slice(0, 10) === value
    ? n
    : NaN;
}
export function addDays(date, n) {
  return new Date(dateNumber(date) + n * 86400000).toISOString().slice(0, 10);
}
export function nights(start, end) {
  return (dateNumber(end) - dateNumber(start)) / 86400000;
}
export function weekDays(date = localDate()) {
  const day = new Date(dateNumber(date)).getUTCDay();
  const monday = addDays(date, -((day + 6) % 7));
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}
export function dateLabel(date) {
  return date?.split("-").reverse().join("/") || "—";
}
export function weekdayLabel(date) {
  if (!Number.isFinite(dateNumber(date))) return "—";
  const day = new Date(dateNumber(date)).getUTCDay();
  return `${day === 0 ? "Chủ nhật" : "Thứ " + ["", "Hai", "Ba", "Tư", "Năm", "Sáu", "Bảy"][day]} · ${dateLabel(date)}`;
}
