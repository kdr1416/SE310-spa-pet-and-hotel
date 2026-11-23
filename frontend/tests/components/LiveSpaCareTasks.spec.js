// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ listCareTasks: vi.fn(), getWorkspace: vi.fn(), performCareAction: vi.fn(), addCareLog: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
import { useAuthStore } from "../../src/stores/auth";
import View from "../../src/components/care/LiveSpaCareTasks.vue";
let pinia;
const pet = (state = "WAITING") => ({ maChiTietDon: 301, tenThuCung: "Milo", trangThai: state, canNangChot: 4, dichVu: [{ tenDichVu: "Tắm", maDichVu: 8 }], nhatKy: [] });
beforeEach(() => {
  vi.resetAllMocks(); pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["CARE_UPDATE", "PHOTO_UPLOAD"] };
  api.listCareTasks.mockResolvedValue({ success: true, data: { items: [pet()], trang: 1, kichThuocTrang: 20, tongSo: 101 } });
  api.performCareAction.mockResolvedValue({ success: true, data: pet("IN_PROGRESS") });
  api.addCareLog.mockResolvedValue({ success: true, data: {} });
});
async function open() { const wrapper = mount(View, { global: { plugins: [pinia] } }); await flushPromises(); await wrapper.get('[data-testid="open-301"]').trigger("click"); return wrapper; }
it("uses E38 for care-only Staff and sends explicit pet action without a parent booking ID", async () => {
  const wrapper = await open();
  expect(wrapper.text()).toContain("101"); expect(api.getWorkspace).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  expect(api.performCareAction).toHaveBeenCalledWith(expect.objectContaining({ maChiTietDon: 301, hanhDong: "START", requestId: expect.any(String) }));
  expect(api.performCareAction.mock.calls[0][0].bookingId).toBeUndefined();
});
it("requires a PAUSE reason and a positive READY photo; logs remain separate commands", async () => {
  api.listCareTasks.mockResolvedValue({ success: true, data: { items: [pet("IN_PROGRESS")], trang: 1, kichThuocTrang: 20, tongSo: 1 } });
  const wrapper = await open();
  await wrapper.get('[data-testid="action"]').setValue("PAUSE");
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  expect(api.performCareAction).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="action"]').setValue("READY");
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  expect(api.performCareAction).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="log-note"]').setValue("Bé bình tĩnh");
  await wrapper.get('[data-testid="log-form"]').trigger("submit"); await flushPromises();
  expect(api.addCareLog).toHaveBeenCalledWith(expect.objectContaining({ maChiTietDon: 301, ghiChu: "Bé bình tĩnh", requestId: expect.any(String) }));
  expect(api.performCareAction).not.toHaveBeenCalled();
});
it("retains the selected snapshot and command after an uncertain result", async () => {
  const wrapper = await open();
  api.performCareAction.mockRejectedValueOnce({ message: "Mất kết nối" });
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  const first = api.performCareAction.mock.calls[0][0];
  await wrapper.get('[data-testid="action-form"]').trigger("submit"); await flushPromises();
  expect(api.performCareAction.mock.calls[1][0].requestId).toBe(first.requestId);
});

