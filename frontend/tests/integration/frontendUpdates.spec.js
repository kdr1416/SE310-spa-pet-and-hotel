import { beforeEach, describe, expect, it } from "vitest";
import { createSeed } from "../../src/services/mock/seed";
import { execute } from "../../src/services/mock/mockOperations";
import { addDays, localDate, weekDays } from "../../src/utils/dates";
import { upgradeMockState } from '../../src/services/mock/bookingAvailability';

let state;
const date = addDays(localDate(), 10);
const payload = (type = "SPA", start = date, end = addDays(start, 2)) => ({
  requestId: crypto.randomUUID(), date: start, shiftId: 1,
  customer: { name: "Khách", phone: "0900000000", email: "booking@example.test" },
  pets: [{ name: "Milo", species: "CHO", weight: 5, type, serviceId: 1,
    roomId: 1, checkout: end, coatType: "LONG" }],
});
const run = (action, p = {}, actor = 1, now = Date.now()) => execute(state, action, p, actor, now);
beforeEach(() => { state = createSeed(); });

describe("approved frontend additions", () => {
  it('upgrades existing data without replacing bookings, photos or configured policy', () => {
    const photo = new Blob(['old-photo'], { type: 'image/png' });
    const b = run('createBooking', payload(), null);
    b.chiTietDonList[0].input.vaccine = photo;
    delete state.featureVersion;
    delete state.settings;
    const bookings = state.bookings;
    upgradeMockState(state);
    expect(state.bookings).toBe(bookings);
    expect(state.bookings[0].chiTietDonList[0].input.vaccine).toBe(photo);
    expect(state.bookings[0].chiTietDonList[0].vaccineVerification).toBeUndefined();
    expect(state.settings.vaccinePolicy.SPA).toBe(false);
    state.settings.vaccinePolicy.SPA = true;
    upgradeMockState(state);
    expect(state.settings.vaccinePolicy.SPA).toBe(true);
  });
  it('does not accept an uploaded image as staff verification or a public check-in actor', () => {
    run('updateVaccinePolicy', { policy: { SPA: true, HOTEL: false, COMBO_SPA_HOTEL: false } });
    const p = payload();
    p.pets[0].vaccine = new Blob(['image'], { type: 'image/png' });
    p.pets[0].vaccineVerified = true;
    const b = run('createBooking', p, null);
    run('reviewBooking', { id: b.id, decision: 'CONFIRMED' }, 2);
    const check = { id: b.id, detailId: b.chiTietDonList[0].id, weight: 5, coat: 'Ổn', employeeId: 3 };
    expect(() => run('checkIn', check, 2)).toThrow(/xác minh/i);
    expect(() => run('checkIn', { ...check, vaccineVerified: true }, null)).toThrow(/quyền/i);
  });
  it('creates useful demo data only for a new demo store', () => {
    expect(createSeed().bookings).toEqual([]);
    const demo = createSeed({ demo: true });
    expect(demo.bookings.some((b) => b.trangThai === 'PENDING')).toBe(true);
    expect(Object.keys(demo.closedDays).length).toBeGreaterThan(0);
    expect(demo.settings.vaccinePolicy.SPA).toBe(false);
  });
  it("accepts a booking without vaccine upload, but enforces configured staff verification at check-in", () => {
    run("updateVaccinePolicy", { policy: { SPA: true, HOTEL: false, COMBO_SPA_HOTEL: false } });
    const b = run("createBooking", payload(), null);
    run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
    const check = { id: b.id, detailId: b.chiTietDonList[0].id, weight: 5, coat: "Không rối", employeeId: 3 };
    expect(() => run("checkIn", check, 2)).toThrow(/xác minh/i);
    expect(b.chiTietDonList[0].checkedIn).toBe(false);
    run("checkIn", { ...check, vaccineVerified: true }, 2);
    expect(b.chiTietDonList[0].vaccineVerification.by).toBe(2);
    expect(b.chiTietDonList[0].thuCung.loaiLong).toBe("LONG");
    expect(b.chiTietDonList[0].tinhTrangLong).toBe("Không rối");
  });
  it("defaults to optional verification and allows hotel-only booking with zero Spa slots", () => {
    state.capacity[`${date}:1`] = 0;
    const b = run("createBooking", payload("HOTEL"), null);
    expect(b.chiTietDonList).toHaveLength(1);
    expect(run("catalog", { date }, null).availability[0].reserved).toBe(0);
    run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
    run("checkIn", { id: b.id, detailId: b.chiTietDonList[0].id, weight: 5, coat: "Ổn", employeeId: 3 }, 2);
  });
  it("checks every Hotel night, counts pets, and does not reserve the checkout night", () => {
    state.catalog.rooms[0].soLuongPhong = 1;
    run("createBooking", payload("HOTEL", date, addDays(date, 1)), null);
    run("createBooking", payload("HOTEL", addDays(date, 1), addDays(date, 2)), null);
    expect(() => run("createBooking", payload("HOTEL"), null)).toThrow(/phòng/i);
    expect(state.bookings).toHaveLength(2);
  });
  it("rejects mismatched checkout dates and overnight bookings with Spa-only pets", () => {
    const p = payload("HOTEL");
    p.pets.push({ ...p.pets[0], name: "Luna", checkout: addDays(date, 3) });
    expect(() => run("createBooking", p, null)).toThrow(/cùng|chung/i);
    p.pets[1] = { ...p.pets[1], type: "SPA" };
    expect(() => run("createBooking", p, null)).toThrow(/tách|phòng|qua đêm/i);
  });
  it("requires a rejection reason and makes duplicate rejection capacity-neutral", () => {
    const b = run("createBooking", payload(), null);
    expect(() => run("reviewBooking", { id: b.id, decision: "CANCELLED" }, 2)).toThrow(/lý do/i);
    const p = { id: b.id, decision: "CANCELLED", reason: "Tiệm không đáp ứng dịch vụ" };
    run("reviewBooking", p, 2);
    run("reviewBooking", p, 2);
    expect(b.loaiHuy).toBe("STAFF_REJECTED");
    expect(b.lyDoHuy).toBe(p.reason);
    expect(run("catalog", { date }, null).availability[0].reserved).toBe(0);
  });
  it("emails only active booking codes to the booking snapshot email, never in the public response", () => {
    const b = run("createBooking", payload(), null);
    b.khachHang.email = 'changed-profile@example.test';
    const known = run("resendTracking", { email: "booking@example.test" }, null);
    const unknown = run("resendTracking", { email: "unknown@example.test" }, null);
    expect(known).toEqual(unknown);
    expect(JSON.stringify(known)).not.toContain(b.trackingCode);
    expect(run("getMailOutbox")).toHaveLength(1);
    expect(run("getMailOutbox")[0].trackingCodes).toEqual([b.trackingCode]);
    expect(() => run("getMailOutbox", {}, 2)).toThrow(/quyền/i);
    run("resendTracking", { email: "booking@example.test" }, null);
    expect(run("getMailOutbox")).toHaveLength(1);
  });
  it("skips completed and cancelled bookings when resending and cannot inject a recipient", () => {
    const b = run("createBooking", payload(), null);
    b.trangThai = "COMPLETED";
    run("resendTracking", { email: "booking@example.test", recipient: "other@example.test" }, null);
    expect(run("getMailOutbox")).toEqual([]);
  });
  it("protects the last active Admin and checks role changes at the service boundary", () => {
    expect(() => run("changeRole", { id: 1, role: "STAFF" })).toThrow(/Admin/i);
    expect(() => run("changeRole", { id: 2, role: "ADMIN" }, 2)).toThrow(/quyền/i);
    run("changeRole", { id: 2, role: "ADMIN" });
    run("changeRole", { id: 1, role: "STAFF" }, 2);
    expect(() => run("getSettings", {}, 1)).toThrow(/quyền/i);
    expect(run("currentUser", {}, 1).vaiTro).toBe("STAFF");
  });
  it("closes arrivals/departures while preserving Hotel stays and Hotel-care shifts", () => {
    const week = addDays(weekDays(date)[0], 14), closed = addDays(week, 2);
    run("updateClosedDays", { week, dates: [closed], reason: "Nghỉ nhận khách" });
    expect(() => run("createBooking", payload("SPA", closed), null)).toThrow(/nghỉ/i);
    expect(() => run("createBooking", payload("HOTEL", addDays(closed, -1), closed), null)).toThrow(/nghỉ/i);
    run("createBooking", payload("HOTEL", addDays(closed, -1), addDays(closed, 1)), null);
    const period = run("createPeriod", { week, openAt: new Date(Date.now()-1000).toISOString(), deadline: new Date(Date.now()+86400000).toISOString(), capacity: 3 });
    expect(() => run("registerShift", { periodId: period.id, date: closed, shiftId: 1 }, 3)).toThrow(/nghỉ|Hotel/i);
    const r = run("registerShift", { periodId: period.id, date: closed, shiftId: 1, purpose: "HOTEL_CARE" }, 3);
    expect(r.purpose).toBe("HOTEL_CARE");
  });
  it("refuses holidays in weeks already opened or conflicting with promised arrivals", () => {
    const period = state.periods[0];
    expect(() => run("updateClosedDays", { week: period.week, dates: [period.week] })).toThrow(/mở/i);
    const week = addDays(period.week, 14);
    run("createBooking", payload("SPA", week), null);
    expect(() => run("updateClosedDays", { week, dates: [week] })).toThrow(/booking|lịch hẹn/i);
    run('togglePeriod', { id: period.id });
    expect(() => run('updateClosedDays', { week: period.week, dates: [period.week] })).toThrow(/mở/i);
  });
});
