import { beforeEach, it, expect } from "vitest";
import { createSeed } from "../../src/services/mock/seed";
import { execute } from "../../src/services/mock/mockOperations";
import { localDate } from "../../src/utils/dates";
let s;
beforeEach(() => {
  s = createSeed();
});
const run = (a, p = {}, user = 1, now = Date.now()) =>
  execute(s, a, p, user, now);
it("validates check-in weight even for hotel-only bookings", () => {
  const b = run(
    "createBooking",
    {
      requestId: crypto.randomUUID(),
      date: localDate(),
      shiftId: 1,
      customer: {
        name: "Demo",
        phone: "0900000000",
        email: "demo@example.test",
      },
      pets: [
        {
          name: "Hotel",
          species: "CHO",
          type: "HOTEL",
          weight: 5,
          roomId: 1,
          checkout: localDate(new Date(Date.now() + 86400000)),
          vaccine: new Blob(["test"], { type: "image/png" }),
        },
      ],
    },
    null,
  );
  run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
  expect(() =>
    run(
      "checkIn",
      {
        id: b.id,
        detailId: b.chiTietDonList[0].id,
        weight: 0,
        coat: "ok",
        employeeId: 3,
      },
      2,
    ),
  ).toThrow(/Cân nặng/);
});
function booking() {
  return run(
    "createBooking",
    {
      requestId: crypto.randomUUID(),
      date: localDate(),
      shiftId: 1,
      customer: {
        name: "Demo",
        phone: "0900000000",
        email: "demo@example.test",
      },
      pets: [
        { name: "Milo", species: "CHO", type: "SPA", weight: 5, serviceId: 1 },
      ],
    },
    null,
  );
}
it("resets a lookup failure window after 15 minutes", () => {
  const b = booking(),
    p = { phone: b.khachHang.soDienThoai, trackingCode: "bad" },
    now = Date.now();
  for (let i = 0; i < 5; i++)
    expect(() => run("lookupBooking", p, null, now)).toThrow();
  expect(() => run("lookupBooking", p, null, now + 900001)).toThrow();
  expect(
    run(
      "lookupBooking",
      { ...p, trackingCode: b.trackingCode },
      null,
      now + 900002,
    ).id,
  ).toBe(b.id);
});
it("updates measured care weight without overwriting declaration or changing agreed price", () => {
  const b = booking(),
    d = b.chiTietDonList[0];
  run("reviewBooking", { id: b.id, decision: "CONFIRMED" }, 2);
  run(
    "checkIn",
    { id: b.id, detailId: d.id, weight: 5, coat: "ok", employeeId: 3 },
    2,
  );
  const price = d.donGiaChot;
  run(
    "careUpdate",
    {
      id: b.id,
      detailId: d.id,
      next: "IN_PROGRESS",
      weight: 7,
      note: "healthy",
      photo: new Blob(["sample"], { type: "image/png" }),
    },
    3,
  );
  expect(d.canNangThucTe).toBe(7);
  expect(d.thuCung.canNangDangKy).toBe(5);
  expect(d.donGiaChot).toBe(price);
});
it("revalidates the entire approved draft before publish", () => {
  const period = s.periods[0];
  for (const shiftId of [1, 2]) {
    const r = run(
      "registerShift",
      { periodId: period.id, date: period.week, shiftId },
      3,
    );
    run("reviewShift", { id: r.id, decision: "APPROVED" });
  }
  s.catalog.shifts[1].gioBatDau = "11:00";
  expect(() => run("publishSchedule", { id: period.id })).toThrow(/trùng/);
  expect(period.published).toBe(false);
});
it("blocks editing a shift into an overlap in an already approved draft", () => {
  const period = s.periods[0];
  for (const shiftId of [1, 2]) {
    const r = run(
      "registerShift",
      { periodId: period.id, date: period.week, shiftId },
      3,
    );
    run("reviewShift", { id: r.id, decision: "APPROVED" });
  }
  expect(() =>
    run("updateShift", { id: 2, start: "11:00", end: "17:00" }),
  ).toThrow(/trùng/);
  expect(s.catalog.shifts[1].gioBatDau).toBe("13:00");
});
