// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import BookingForm from "../../src/components/booking/BookingForm.vue";
import { catalogService } from '../../src/services/api/catalogService';
vi.mock("../../src/services/api/catalogService", () => ({
  catalogService: {
    catalog: vi.fn(async () => ({
      data: {
        services: [
          {
            id: 1,
            tenDichVu: "Bath",
            giaCoBan: 100000,
            isActive: true,
            loaiDichVu: "SPA_BASIC",
          },
        ],
        combos: [],
        rooms: [],
        shifts: [{ id: 1, tenCa: "Morning", isActive: true }],
        availability: [{ id: 1, tenCa: "Morning", left: 4 }],
      },
    })),
  },
}));
vi.mock("../../src/services/api/bookingService", () => ({
  createCustomerBooking: vi.fn(async () => {
    throw new Error("Slot vừa hết.");
  }),
  bookingService: { createBooking: vi.fn() },
}));
beforeEach(() => setActivePinia(createPinia()));
describe("booking interactions", () => {
  it('shows closed days before the Spa customer chooses a date', async () => {
    const catalog = (await catalogService.catalog()).data;
    catalog.roomAvailability = [{ date: '2026-10-20', closed: true, rooms: [] }];
    catalogService.catalog.mockResolvedValueOnce({ data: catalog });
    const w = mount(BookingForm);
    await flushPromises();
    const closedDay = w.findAll('button').find((b) => b.text().includes('Nghỉ tiếp nhận'));
    expect(closedDay).toBeTruthy();
    expect(closedDay.attributes('disabled')).toBeDefined();
    w.unmount();
  });
  it('offers optional vaccine upload for Spa and a separate coat type', async () => {
    const w = mount(BookingForm);
    await flushPromises();
    expect(w.get('input[type=file]').attributes('required')).toBeUndefined();
    expect(w.text()).toContain('Ảnh giấy tiêm chủng (tùy chọn)');
    expect(w.text()).toContain('Loại lông');
    w.unmount();
  });
  it("requires at least one pet and lets customer add another", async () => {
    const w = mount(BookingForm);
    await flushPromises();
    expect(w.findAll("[data-testid=pet-card]")).toHaveLength(1);
    expect(
      w.get("[data-testid=remove-pet]").attributes("disabled"),
    ).toBeDefined();
    await w.get("[data-testid=add-pet]").trigger("click");
    expect(w.findAll("[data-testid=pet-card]")).toHaveLength(2);
  });
  it("preserves values and reports service errors on failed submit", async () => {
    const w = mount(BookingForm);
    await flushPromises();
    await w.get("#customer-name").setValue("Demo");
    await w.get("#customer-phone").setValue("0900000000");
    await w.get("#customer-email").setValue("demo@example.test");
    await w.get("[data-testid=pet-name]").setValue("Milo");
    await w.get("form").trigger("submit");
    await flushPromises();
    expect(w.text()).toContain("Slot vừa hết.");
    expect(w.get("#customer-name").element.value).toBe("Demo");
    expect(w.emitted("created")).toBeUndefined();
  });
});
