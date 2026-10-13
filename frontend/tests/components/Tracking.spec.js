// @vitest-environment jsdom
import { it, expect, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia } from "pinia";
import Tracking from "../../src/views/customer/CustomerTrackingView.vue";
const lookup = vi.hoisted(() => vi.fn());
vi.mock("../../src/services/api/bookingService", () => ({
  bookingService: { lookupBooking: lookup },
}));
vi.mock("../../src/services/api/catalogService", () => ({
  catalogService: { catalog: async () => ({ data: { services: [] } }) },
}));
vi.mock("vue-router", () => ({
  useRoute: () => ({ query: { code: "TRK-DEMO" } }),
}));
it("removes the prior result when a subsequent lookup fails", async () => {
  lookup
    .mockResolvedValueOnce({
      data: {
        id: "demo",
        trackingCode: "TRK-DEMO",
        trangThai: "PENDING",
        khachHang: { tenKhachHang: "Demo", soDienThoai: "0900000000" },
        chiTietDonList: [],
        requests: [],
      },
    })
    .mockRejectedValueOnce(new Error("Thông tin tra cứu không chính xác."));
  const w = mount(Tracking, {
    global: { plugins: [createPinia()], stubs: { RouterLink: true } },
  });
  await flushPromises();
  await w.get("#tracking-phone").setValue("0900000000");
  await w.get("[data-testid=lookup-form]").trigger("submit");
  await flushPromises();
  expect(w.find("[data-testid=tracking-result]").exists()).toBe(true);
  await w.get("#tracking-code").setValue("bad");
  await w.get("[data-testid=lookup-form]").trigger("submit");
  await flushPromises();
  expect(w.find("[data-testid=tracking-result]").exists()).toBe(false);
  expect(w.text()).toContain("Thông tin tra cứu không chính xác.");
  w.unmount();
});
