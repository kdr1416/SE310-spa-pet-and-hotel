import { addDays, localDate } from "./dates";
import { canAccess } from "./permissions";

const open = b => !["COMPLETED", "CANCELLED"].includes(b.trangThai);
export function dashboardSummary(bookings, user, date = new Date(), ledger = []) {
  const today = typeof date === "string" ? date : localDate(date);
  const active = bookings.filter(open);
  const arrivals = bookings.filter(b => b.date === today && b.trangThai !== "CANCELLED");
  const details = active.flatMap(booking => (booking.chiTietDonList || []).map(detail => ({ booking, detail })));
  const tasks = details.filter(({ detail }) => detail.checkedIn && detail.nhanVienId === user?.id);
  const net = canAccess(user, "FINANCE_MANAGE")
    ? ledger.filter(l => l.date === today).reduce((n, l) => n + (l.type === "PAYMENT" ? l.amount : l.type === "REFUND" ? -l.amount : 0), 0)
    : null;
  return {
    today, arrivals, tasks, net,
    pending: active.filter(b => b.trangThai === "PENDING"),
    ready: active.filter(b => b.trangThai === "READY"),
    caring: details.filter(({ detail }) => detail.checkedIn && detail.trangThaiChamSoc === "IN_PROGRESS").length,
    awaitingCheckIn: arrivals.filter(b => ["CONFIRMED", "IN_PROGRESS", "READY"].includes(b.trangThai)).reduce((n, b) => n + b.chiTietDonList.filter(d => !d.checkedIn && !d.laDichVuThem).length, 0),
    trend: Array.from({ length: 7 }, (_, i) => {
      const day = addDays(today, i - 6);
      return { date: day, count: bookings.filter(b => b.date === day && b.trangThai !== "CANCELLED").length };
    }),
  };
}

export function publishedShifts(schedule, userId, today) {
  return (schedule.assignments || []).filter(s => s.employeeId === userId && s.date === today && s.status === "APPROVED" && schedule.periods?.some(p => p.id === s.periodId && p.published));
}
