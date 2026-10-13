import { describe, it, expect } from "vitest";
import { dashboardSummary, publishedShifts } from "../../src/utils/dashboardSummary";

const today = "2026-10-13";
const pet = (id, state, employee = 3, checkedIn = true) => ({
  id, trangThaiChamSoc: state, nhanVienId: employee, checkedIn,
  thuCung: { tenThuCung: id },
});
const booking = (id, status, date, pets) => ({
  id, date, trangThai: status, createdAt: "2026-10-01T00:00:00Z", chiTietDonList: pets,
});
describe("overview summary", () => {
  it("counts reception dates, backlog and pets independently of creation date", () => {
    const bookings = [booking("a", "IN_PROGRESS", today, [pet("1", "WAITING", null, false), pet("2", "WAITING")]), booking("b", "PENDING", "2026-10-12", []), booking("c", "READY", today, []), booking("d", "CANCELLED", today, [])];
    const result = dashboardSummary(bookings, { vaiTro: "STAFF", workGroup: "RECEPTION" }, today);
    expect([result.arrivals.length, result.pending.length, result.awaitingCheckIn, result.ready.length]).toEqual([2, 1, 1, 1]);
    expect(result.trend.at(-1).count).toBe(2);
  });
  it("only includes assigned checked-in pets in open bookings for Spa", () => {
    const bookings = [booking("a", "IN_PROGRESS", today, [pet("mine", "WAITING"), pet("other", "IN_PROGRESS", 4), pet("not-in", "WAITING", 3, false), pet("ready", "READY")]), booking("closed", "COMPLETED", today, [pet("done", "IN_PROGRESS")])];
    const result = dashboardSummary(bookings, { id: 3, vaiTro: "STAFF", workGroup: "SPA" }, today, [{ date: today, type: "PAYMENT", amount: 100 }]);
    expect(result.tasks.map(t => t.detail.id)).toEqual(["mine", "ready"]);
    expect(result.net).toBeNull();
  });
  it("uses Vietnam today and ledger refunds rather than booking estimates", () => {
    const result = dashboardSummary([], { vaiTro: "ADMIN" }, new Date("2026-10-12T18:00:00Z"), [{ date: today, type: "PAYMENT", amount: 200 }, { date: today, type: "REFUND", amount: 50 }, { date: "2026-10-12", type: "PAYMENT", amount: 999 }]);
    expect(result.today).toBe(today);
    expect(result.net).toBe(150);
    expect(result.trend).toHaveLength(7);
  });
  it("only exposes own approved published shifts for today", () => {
    const schedule = { periods: [{ id: 1, published: true }, { id: 2, published: false }], assignments: [{ id: "yes", employeeId: 3, periodId: 1, status: "APPROVED", date: today }, { id: "draft", employeeId: 3, periodId: 2, status: "APPROVED", date: today }, { id: "other", employeeId: 4, periodId: 1, status: "APPROVED", date: today }] };
    expect(publishedShifts(schedule, 3, today).map(s => s.id)).toEqual(["yes"]);
  });
});
