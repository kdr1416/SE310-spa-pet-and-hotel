import { describe, expect, it } from "vitest";
import { canAccess } from "../../src/utils/permissions";
import { estimatePet, weightFactor } from "../../src/utils/pricing";
import { localDate, addDays, weekDays } from "../../src/utils/dates";
import {
  deriveBookingStatus,
  validateBooking,
} from "../../src/utils/bookingRules";

const catalog = {
  services: [
    { id: 1, giaCoBan: 100000, isActive: true, loaiDichVu: "SPA_BASIC" },
  ],
  combos: [],
  rooms: [{ id: 1, giaTheoNgay: 200000, isActive: true }],
};
describe("permissions (temporary work groups, unchanged STAFF role)", () => {
  it("keeps a legacy STAFF dashboard reachable without inventing financial permissions", () => {
    const user = { vaiTro: "STAFF", isActive: true };
    expect(canAccess(user, "SCHEDULE_SELF")).toBe(true);
    expect(canAccess(user, "BOOKING_MANAGE")).toBe(false);
  });
  it("spa can read bookings, cannot manage payments; reception cannot manage finance", () => {
    const spa = { vaiTro: "STAFF", workGroup: "SPA", isActive: true };
    expect(canAccess(spa, "BOOKING_READ")).toBe(true);
    expect(canAccess(spa, "BOOKING_MANAGE")).toBe(false);
    expect(
      canAccess({ ...spa, workGroup: "RECEPTION" }, "FINANCE_MANAGE"),
    ).toBe(false);
    expect(
      canAccess({ vaiTro: "ADMIN", isActive: false }, "STAFF_MANAGE"),
    ).toBe(false);
    expect(canAccess(null, "BOOKING_READ")).toBe(false);
  });
});
describe("prices and calendar days", () => {
  it.each([
    [4.999, 1],
    [5, 1.3],
    [9.999, 1.3],
    [10, 1.6],
    [20, 2],
  ])("weight %s has factor %s", (w, factor) =>
    expect(weightFactor(w)).toBe(factor),
  );
  it("prices spa by weight and hotel by nights, separately per pet", () => {
    expect(
      estimatePet(
        { serviceId: 1, weight: 5, type: "SPA" },
        "2026-10-13",
        catalog,
      ).amount,
    ).toBe(130000);
    expect(
      estimatePet(
        { roomId: 1, weight: 5, type: "HOTEL", checkout: "2026-10-16" },
        "2026-10-13",
        catalog,
      ).amount,
    ).toBe(600000);
  });
  it("uses Vietnam date at UTC midnight boundary and Monday calendar across month", () => {
    expect(localDate(new Date("2026-10-12T18:00:00Z"))).toBe("2026-10-13");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(weekDays("2026-11-01")).toEqual([
      "2026-10-26",
      "2026-10-27",
      "2026-10-28",
      "2026-10-29",
      "2026-10-30",
      "2026-10-31",
      "2026-11-01",
    ]);
  });
});
describe("multi-pet rules", () => {
  it("does not mark booking READY when another pet is WAITING", () => {
    expect(
      deriveBookingStatus(
        [{ trangThaiChamSoc: "READY" }, { trangThaiChamSoc: "WAITING" }],
        "IN_PROGRESS",
      ),
    ).toBe("IN_PROGRESS");
  });
  it("rejects missing customer phone, empty pets and over-capacity", () => {
    const payload = {
      customer: { name: "Demo", email: "demo@example.test", phone: "" },
      date: localDate(),
      pets: [],
    };
    expect(validateBooking(payload, 1, catalog).length).toBeGreaterThan(0);
    payload.customer.phone = "0900000000";
    payload.pets = [1, 2].map(() => ({
      name: "Pet",
      species: "CHO",
      type: "SPA",
      serviceId: 1,
      weight: 5,
    }));
    expect(validateBooking(payload, 1, catalog).join(" ")).toMatch(/chỗ/);
  });
});
