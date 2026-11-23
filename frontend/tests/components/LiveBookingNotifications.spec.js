// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ getWorkspace: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
vi.mock("vue-router", () => ({ useRouter: () => ({ replace: vi.fn() }) }));
import { useAuthStore } from "../../src/stores/auth";
import { useWorkspaceStore } from "../../src/stores/workspace";
import Notifications from "../../src/components/shared/BookingNotifications.vue";
it("shows the server pending count even when only one row is loaded", async () => {
  const pinia = createPinia(); setActivePinia(pinia); const auth = useAuthStore();
  auth.user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["BOOKING_APPROVE"] }; auth.token = "test-access-token";
  vi.spyOn(auth, "initAuth").mockResolvedValue();
  api.getWorkspace.mockResolvedValue({ success: true, data: { bookings: [{ id: 101, trangThai: "PENDING", trackingCode: "ABCD1234", date: "2035-01-01", khachHang: { tenKhachHang: "Lan" } }], tongSo: 125 } });
  const wrapper = mount(Notifications, { global: { plugins: [pinia], stubs: { RouterLink: true } } });
  await flushPromises(); expect(wrapper.get("button").text()).toContain("125"); wrapper.unmount();
});
it("clears account-specific notifications before a switched account's slow refresh", async () => {
  const pinia = createPinia(); setActivePinia(pinia); const auth = useAuthStore();
  auth.user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["BOOKING_APPROVE"] }; auth.token = "test-access-token";
  vi.spyOn(auth, "initAuth").mockResolvedValue();
  api.getWorkspace.mockResolvedValueOnce({ success: true, data: { bookings: [{ id: 101, trangThai: "PENDING", trackingCode: "PRIVATE1", date: "2035-01-01", khachHang: { tenKhachHang: "Lan" } }], tongSo: 125 } });
  const wrapper = mount(Notifications, { global: { plugins: [pinia], stubs: { RouterLink: true } } }); await flushPromises();
  let resolveOld;
  api.getWorkspace.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
  await wrapper.get("button").trigger("click"); await flushPromises();
  auth.user = { ...auth.user, maNhanVien: 73 }; await flushPromises();
  expect(wrapper.get("button").text()).not.toContain("125");
  resolveOld({ success: true, data: { bookings: [{ id: 101, trangThai: "PENDING", trackingCode: "PRIVATE1" }], tongSo: 125 } }); await flushPromises();
  expect(wrapper.get("button").text()).not.toContain("125"); wrapper.unmount();
});
it("invalidates the shared private workspace when notifications receive an authoritative denial", async () => {
  const pinia = createPinia(); setActivePinia(pinia); const auth = useAuthStore();
  auth.user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["BOOKING_APPROVE"] }; auth.token = "test-access-token";
  vi.spyOn(auth, "initAuth").mockResolvedValue();
  const workspace = useWorkspaceStore(); workspace.bookings = [{ maDon: 101 }];
  api.getWorkspace.mockRejectedValueOnce({ errorCode: "FORBIDDEN" });
  const wrapper = mount(Notifications, { global: { plugins: [pinia], stubs: { RouterLink: true } } }); await flushPromises();
  expect(workspace.bookings).toEqual([]); wrapper.unmount();
});
