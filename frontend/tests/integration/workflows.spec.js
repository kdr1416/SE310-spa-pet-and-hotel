import { beforeEach, describe, expect, it } from "vitest";
import { createSeed } from "../../src/services/mock/seed";
import { execute } from "../../src/services/mock/mockOperations";
import { addDays, localDate } from "../../src/utils/dates";
let db;
const photo = () => new Blob(["test-image"], { type: "image/png" });
const payload = (count = 2) => ({
  requestId: crypto.randomUUID(),
  customer: { name: "Test", phone: "0900000000", email: "test@example.test" },
  date: localDate(),
  shiftId: 1,
  pets: Array.from({ length: count }, (_, i) => ({
    name: "Pet " + i,
    species: "CHO",
    weight: 5,
    type: "SPA",
    serviceId: 1,
  })),
});
const run = (name, p = {}, actor = 1) => execute(db, name, p, actor);
const create = (count = 2) => run("createBooking", payload(count), null);
const credentials = (b) => ({
  trackingCode: b.trackingCode,
  phone: b.khachHang.soDienThoai,
});
beforeEach(() => {
  db = createSeed();
  db.bookings = [];
  db.requests = [];
  db.ledger = [];
});
describe("bookings and lookup", () => {
  it("reserves by pets, deduplicates submission and releases exactly once on rejection", () => {
    db.capacity[`${localDate()}:1`] = 2;
    const p = payload(),
      b = run("createBooking", p, null);
    expect(run("createBooking", p, null).id).toBe(b.id);
    expect(() => create(1)).toThrow(/chỗ/);
    const rejection = { id: b.id, decision: "CANCELLED", reason: "Tiệm không đáp ứng được" };
    run("reviewBooking", rejection, 2);
    expect(run("reviewBooking", rejection, 2).id).toBe(b.id);
    expect(create(2).chiTietDonList).toHaveLength(2);
  });
  it("checks both phone/code and blocks brute force with a generic error", () => {
    const b = create();
    expect(run("lookupBooking", credentials(b), null).id).toBe(b.id);
    for (let i = 0; i < 6; i++)
      expect(() =>
        run("lookupBooking", { ...credentials(b), trackingCode: "bad" }, null),
      ).toThrow(/tra cứu/);
    expect(() => run("lookupBooking", credentials(b), null)).toThrow(/15 phút/);
  });
  it("rejects receptionist changes from spa and system changes from reception", () => {
    expect(() => run("createBooking", payload(), 3)).toThrow(/quyền/);
    expect(() =>
      run("updateCapacity", { date: localDate(), shiftId: 1, total: 3 }, 2),
    ).toThrow(/quyền/);
  });
  it("cannot reduce slots below existing reservations", () => {
    create(2);
    expect(() =>
      run("updateCapacity", { date: localDate(), shiftId: 1, total: 1 }),
    ).toThrow();
  });
});
function readyBooking() {
  const b = create();
  run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
  for (const d of b.chiTietDonList) {
    run(
      "checkIn",
      {
        id: b.id,
        detailId: d.id,
        weight: 5,
        coat: "Bình thường",
        employeeId: 3,
      },
      2,
    );
    run(
      "careUpdate",
      {
        id: b.id,
        detailId: d.id,
        next: "IN_PROGRESS",
        note: "Healthy",
        photo: photo(),
      },
      3,
    );
    run(
      "careUpdate",
      { id: b.id, detailId: d.id, next: "READY", note: "Done", photo: photo() },
      3,
    );
  }
  return db.bookings.find((x) => x.id === b.id);
}
describe("care and payments", () => {
  it("enforces assignment and initial photo/note before IN_PROGRESS; all pets must be ready", () => {
    const b = create();
    run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
    const d = b.chiTietDonList[0];
    run(
      "checkIn",
      { id: b.id, detailId: d.id, weight: 5, coat: "Normal", employeeId: 3 },
      2,
    );
    expect(() =>
      run(
        "careUpdate",
        { id: b.id, detailId: d.id, next: "IN_PROGRESS", note: "Healthy" },
        3,
      ),
    ).toThrow(/ảnh|Ảnh/);
    expect(() =>
      run(
        "careUpdate",
        { id: b.id, detailId: d.id, next: "IN_PROGRESS", photo: photo() },
        3,
      ),
    ).toThrow(/ghi chú/);
    run(
      "careUpdate",
      {
        id: b.id,
        detailId: d.id,
        next: "IN_PROGRESS",
        note: "Healthy",
        photo: photo(),
      },
      3,
    );
    run(
      "careUpdate",
      { id: b.id, detailId: d.id, next: "READY", note: "Done", photo: photo() },
      3,
    );
    expect(db.bookings[0].trangThai).toBe("IN_PROGRESS");
    expect(() => run("createInvoice", { id: b.id }, 2)).toThrow(/sẵn sàng/);
  });
  it("rejects missing invoices, snapshots prices, cannot pay twice or mutate paid data", () => {
    const b = readyBooking();
    expect(() =>
      run("confirmPayment", { id: b.id, method: "CASH" }, 2),
    ).toThrow(/hóa đơn/);
    const invoice = run("createInvoice", { id: b.id }, 2);
    const total = invoice.tongThanhToan;
    run("updatePrice", { kind: "services", id: 1, price: 900000 });
    expect(db.bookings[0].hoaDon.tongThanhToan).toBe(total);
    run("confirmPayment", { id: b.id, method: "CASH" }, 2);
    expect(() =>
      run("confirmPayment", { id: b.id, method: "CASH" }, 2),
    ).toThrow();
    expect(() =>
      run(
        "checkIn",
        { id: b.id, detailId: b.chiTietDonList[0].id, weight: 1 },
        2,
      ),
    ).toThrow();
  });
  it("keeps an addon pending until approved; blocks reviews early or duplicated", () => {
    const b = create();
    expect(() =>
      run(
        "customerRequest",
        { ...credentials(b), type: "REVIEW", note: "good", rating: 5 },
        null,
      ),
    ).toThrow();
    run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
    const req = run(
      "customerRequest",
      {
        ...credentials(b),
        type: "ADDON",
        detailId: b.chiTietDonList[0].id,
        serviceId: 3,
        note: "add",
      },
      null,
    );
    expect(db.bookings[0].chiTietDonList).toHaveLength(2);
    run("reviewRequest", { id: req.id, decision: "APPROVED" }, 2);
    expect(db.bookings[0].chiTietDonList).toHaveLength(3);
  });
  it("allows exactly one completed review, hides it from staff, prevents excessive/double refunds", () => {
    const b = readyBooking();
    run("createInvoice", { id: b.id }, 2);
    run("confirmPayment", { id: b.id, method: "CASH" }, 2);
    run(
      "customerRequest",
      { ...credentials(b), type: "REVIEW", note: "great", rating: 5 },
      null,
    );
    expect(() =>
      run(
        "customerRequest",
        { ...credentials(b), type: "REVIEW", note: "again", rating: 4 },
        null,
      ),
    ).toThrow();
    expect(
      run("getWorkspace", {}, 2).requests.some((r) => r.type === "REVIEW"),
    ).toBe(false);
    const r = run(
      "customerRequest",
      { ...credentials(b), type: "REFUND", note: "reason" },
      null,
    );
    expect(() =>
      run("reviewRequest", {
        id: r.id,
        decision: "APPROVED",
        amount: b.hoaDon.daThanhToan + 1,
      }),
    ).toThrow();
    run("reviewRequest", { id: r.id, decision: "APPROVED", amount: 10000 });
    expect(() =>
      run("reviewRequest", { id: r.id, decision: "APPROVED", amount: 10000 }),
    ).toThrow();
    expect(db.ledger.filter((l) => l.type === "REFUND")).toHaveLength(1);
  });
});
describe("schedule and staff", () => {
  it("blocks registration at deadline, duplicate registration, unpublished staff schedule", () => {
    const p = db.periods[0];
    const r = { periodId: p.id, date: p.week, shiftId: 1 };
    run("registerShift", r, 2);
    expect(() => run("registerShift", r, 2)).toThrow();
    expect(run("getSchedule", {}, 2).assignments).toHaveLength(0);
    p.deadline = new Date(Date.now() - 1000).toISOString();
    expect(() =>
      run("registerShift", { ...r, date: addDays(p.week, 1) }, 3),
    ).toThrow(/đóng|hạn/);
  });
  it("prevents overlapping assignments, respects capacity, publishes staff schedule", () => {
    const p = db.periods[0];
    const a = run(
      "registerShift",
      { periodId: p.id, date: p.week, shiftId: 1 },
      2,
    );
    run("reviewShift", { id: a.id, decision: "APPROVED" });
    const duplicate = run(
      "registerShift",
      { periodId: p.id, date: p.week, shiftId: 2 },
      2,
    );
    db.catalog.shifts[1].gioBatDau = "11:00";
    expect(() =>
      run("reviewShift", { id: duplicate.id, decision: "APPROVED" }),
    ).toThrow(/trùng/);
    run("publishSchedule", { id: p.id });
    expect(run("getSchedule", {}, 2).assignments).toHaveLength(1);
    expect(() =>
      run("reviewShift", { id: duplicate.id, decision: "APPROVED" }),
    ).toThrow(/chốt/);
  });
  it("rejects duplicate usernames and assignments to inactive or reception users", () => {
    expect(() =>
      run("createStaff", { name: "User", username: "staff", workGroup: "SPA" }),
    ).toThrow(/trùng|tồn tại/);
    const b = create();
    run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
    expect(() =>
      run(
        "checkIn",
        {
          id: b.id,
          detailId: b.chiTietDonList[0].id,
          weight: 5,
          coat: "ok",
          employeeId: 2,
        },
        2,
      ),
    ).toThrow();
    db.users[2].isActive = false;
    expect(() =>
      run(
        "checkIn",
        {
          id: b.id,
          detailId: b.chiTietDonList[0].id,
          weight: 5,
          coat: "ok",
          employeeId: 3,
        },
        2,
      ),
    ).toThrow();
  });
});
