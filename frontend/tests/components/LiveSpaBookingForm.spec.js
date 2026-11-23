// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { beforeEach, afterEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ catalog: vi.fn(), getBookingOptions: vi.fn(), callOperation: vi.fn(), createCustomerBooking: vi.fn() }));
vi.mock("../../src/services/api/catalogService", () => ({ catalogService: api }));
vi.mock("../../src/services/api/operationClient", () => ({ callOperation: api.callOperation }));
vi.mock("../../src/services/api/bookingService", () => ({ createCustomerBooking: api.createCustomerBooking }));
import LiveSpaBookingForm from "../../src/components/booking/LiveSpaBookingForm.vue";
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2035-01-01T01:00:00Z"));
  vi.resetAllMocks();
  api.catalog.mockResolvedValue({ success: true, data: { services: [{ id: 8, tenDichVu: "Tắm", isActive: true, loaiDichVu: "SPA_BASIC", bangGia: [] }], khoangSize: [], combos: [], rooms: [] } });
  api.getBookingOptions.mockImplementation(async ({ ngay }) => ({ success: true, data: { ngay, ca: [
    { maCa: 1, maKhungGio: ngay === "2035-01-01" ? 701 : 899, tenCa: "Sáng", gioBatDau: "09:00", gioKetThuc: "12:00", moNhanKhach: true },
    { maCa: 2, maKhungGio: null, tenCa: "Chiều", gioBatDau: "13:00", gioKetThuc: "17:00", moNhanKhach: false },
  ] } }));
  api.callOperation.mockResolvedValue({ success: true, data: { coTheDat: true } });
  api.createCustomerBooking.mockResolvedValue({ success: true, data: { trackingCode: "ABCD1234", tongTamTinh: 100, emailSent: false } });
});
afterEach(() => vi.useRealTimers());
async function fill(wrapper) {
  await flushPromises();
  for (const [field, value] of Object.entries({ name: "Lan", phone: "0912345678", email: "lan@example.test", "pet-name": "Milo", weight: "4", service: "8", shift: "1" }))
    await wrapper.get(`[data-testid="${field}"]`).setValue(value);
}
it("uses real day-specific slots for every service and preserves tracking after email failure", async () => {
  const wrapper = mount(LiveSpaBookingForm); await fill(wrapper);
  expect(wrapper.text()).toContain("Chưa có giá");
  expect(wrapper.get('[data-testid="shift"] option[value="2"]').attributes("disabled")).toBeDefined();
  await wrapper.get('[data-testid="date"]').setValue("2035-01-02"); await flushPromises();
  await wrapper.get('[data-testid="shift"]').setValue("1");
  await wrapper.get('[data-testid="availability"]').trigger("click"); await flushPromises();
  expect(api.callOperation).toHaveBeenCalledWith("checkAvailability", expect.objectContaining({ maKhungGio: 899, ngayTra: "2035-01-02", thuCung: [expect.objectContaining({ dichVu: [{ maDichVu: 8, maKhungGio: 899 }] })] }), true);
  await wrapper.get("form").trigger("submit"); await flushPromises();
  expect(api.createCustomerBooking).toHaveBeenCalledWith(expect.objectContaining({ maKhungGio: 899, ngayTra: "2035-01-02", requestId: expect.any(String) }));
  expect(wrapper.emitted("created")[0][0].trackingCode).toBe("ABCD1234");
});
it("requires a fresh capacity check after input changes and retains the command on uncertain retry", async () => {
  const wrapper = mount(LiveSpaBookingForm); await fill(wrapper);
  await wrapper.get('[data-testid="availability"]').trigger("click"); await flushPromises();
  await wrapper.get('[data-testid="weight"]').setValue("5");
  await wrapper.get("form").trigger("submit"); await flushPromises();
  expect(api.createCustomerBooking).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="availability"]').trigger("click"); await flushPromises();
  api.createCustomerBooking.mockRejectedValueOnce({ message: "Mất kết nối", errorCode: "TEMPORARY_FAILURE" });
  await wrapper.get("form").trigger("submit"); await flushPromises();
  const first = api.createCustomerBooking.mock.calls[0][0];
  await wrapper.get("form").trigger("submit"); await flushPromises();
  expect(api.createCustomerBooking.mock.calls[1][0].requestId).toBe(first.requestId);
});
it("ignores late options for the previous day", async () => {
  let resolveOld;
  api.getBookingOptions.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
  const wrapper = mount(LiveSpaBookingForm); await flushPromises();
  await wrapper.get('[data-testid="date"]').setValue("2035-01-02"); await flushPromises();
  resolveOld({ success: true, data: { ca: [{ maCa: 99, maKhungGio: 111, tenCa: "Cũ", moNhanKhach: true }] } });
  await flushPromises();
  expect(wrapper.text()).not.toContain("Cũ");
  expect(wrapper.text()).toContain("Sáng");
});
it("keeps vaccine File ownership and rejects invalid uploads visibly", async () => {
  const wrapper = mount(LiveSpaBookingForm); await fill(wrapper);
  const input = wrapper.get('input[type="file"]');
  const file = new File(["image"], "vaccine.jpg", { type: "image/jpeg" });
  Object.defineProperty(input.element, "files", { value: [file], configurable: true });
  await input.trigger("change");
  await wrapper.get('[data-testid="availability"]').trigger("click"); await flushPromises();
  await wrapper.get("form").trigger("submit"); await flushPromises();
  expect(api.createCustomerBooking.mock.calls[0][0].thuCung[0].vaccinationFile).toBe(file);
  Object.defineProperty(input.element, "files", { value: [new File(["bad"], "bad.txt", { type: "text/plain" })], configurable: true });
  await input.trigger("change"); await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("JPEG/PNG/WebP");
});
