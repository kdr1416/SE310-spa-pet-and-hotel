// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
let client, pinia, useAuthStore;
beforeEach(async () => {
  vi.resetModules(); vi.stubEnv("VITE_USE_MOCK_API", "false");
  localStorage.clear(); sessionStorage.clear();
  client = (await import("../../src/services/api/httpClient")).default;
  ({ useAuthStore } = await import("../../src/stores/auth"));
  pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["CARE_UPDATE", "INVOICE_CREATE", "INVOICE_COLLECT", "PET_HANDOVER"] };
});
afterEach(() => { vi.unstubAllEnvs(); vi.restoreAllMocks(); });
function respond(config, data) { return { config, status: 200, statusText: "OK", headers: {}, data: { success: true, data, message: "OK" } }; }
it("connects care-only UI through E38 and the real multipart E17 adapter", async () => {
  let state = "WAITING";
  const pet = () => ({ maChiTietDon: 301, tenThuCung: "Milo", trangThai: state, dichVu: [{ maDichVu: 8, tenDichVu: "Tắm" }], nhatKy: [] });
  const transport = vi.fn(async config => {
    if (config.method === "get" && config.url === "/internal/care-tasks") return respond(config, { items: [pet()], trang: 1, kichThuocTrang: 20, tongSo: 1 });
    if (config.method === "post" && config.url === "/internal/booking-pets/301/care-actions") {
      expect(config.data).toBeInstanceOf(FormData);
      expect(JSON.parse(config.data.get("payload"))).toMatchObject({ hanhDong: "START", requestId: expect.any(String) });
      state = "IN_PROGRESS"; return respond(config, pet());
    }
    throw new Error(`Unexpected transport ${config.method} ${config.url}`);
  });
  client.defaults.adapter = transport;
  const View = (await import("../../src/components/care/LiveSpaCareTasks.vue")).default;
  const wrapper = mount(View, { global: { plugins: [pinia] } }); await flushPromises();
  await wrapper.get('[data-testid="open-301"]').trigger("click");
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  expect(wrapper.text()).toContain("IN_PROGRESS");
  expect(transport.mock.calls.some(([config]) => config.url === "/internal/bookings")).toBe(false);
  expect(wrapper.find('[data-testid="log-form"]').exists()).toBe(false);
  wrapper.unmount();
});
it("connects real invoice and handover adapters using three distinct IDs", async () => {
  let paid = false;
  const invoice = () => ({ maHoaDon: 902, trangThai: paid ? "PAID" : "UNPAID", dongTien: [], tongTien: 100, conPhaiTra: 100, cocKhauTru: 0, qr: null });
  const pet = { maChiTietDon: 301, tenThuCung: "Milo", trangThai: "READY", dichVu: [], nhatKy: [] };
  const transport = vi.fn(async config => {
    const payload = JSON.parse(config.data);
    if (config.url === "/internal/bookings/101/invoices") return respond(config, invoice());
    if (config.url === "/internal/invoices/902/checkout") { expect(payload).toMatchObject({ soTienThucNhan: 100, soTienDaTraLai: 0 }); paid = true; return respond(config, invoice()); }
    if (config.url === "/internal/booking-pets/301/handover") { expect(paid).toBe(true); expect(payload.xacNhanDaNhan).toBe(true); return respond(config, { ...pet, trangThai: "DONE" }); }
    throw new Error(`Unexpected transport ${config.url}`);
  });
  client.defaults.adapter = transport;
  const View = (await import("../../src/components/booking/LiveSpaInvoice.vue")).default;
  const wrapper = mount(View, { props: { booking: { maDon: 101, trangThai: "IN_PROGRESS", thuCung: [pet] } }, global: { plugins: [pinia] } });
  await wrapper.get('[data-testid="create-invoice"]').trigger("click"); await flushPromises();
  await wrapper.get('[data-testid="received"]').setValue("100"); await wrapper.get('[data-testid="change"]').setValue("0");
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  expect(transport).toHaveBeenCalledTimes(2);
  await wrapper.get('[data-testid="handover-consent-301"]').setValue(true);
  await wrapper.get('[data-testid="handover-form-301"]').trigger("submit"); await flushPromises();
  expect(transport).toHaveBeenCalledTimes(3);
  expect(wrapper.emitted("changed").at(-1)[0]).toMatchObject({ trangThai: "COMPLETED", reloadOnly: true });
  wrapper.unmount();
});
