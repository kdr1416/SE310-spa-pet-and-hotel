// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ getWorkspace: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
vi.mock("vue-router", () => ({ useRoute: () => ({ query: {} }) }));
import { useAuthStore } from "../../src/stores/auth";
import Workspace from "../../src/components/booking/LiveBookingWorkspace.vue";
let pinia;
beforeEach(() => {
  pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["BOOKING_APPROVE", "CHECKIN_PERFORM"] };
  api.getWorkspace.mockImplementation(async query => ({ success: true, data: {
    bookings: [{ id: query.trang === 2 ? 102 : 101, maDon: query.trang === 2 ? 102 : 101, trackingCode: "ABCD1234", trangThai: query.trangThai, thuCung: [], date: "2035-01-01" }], users: [], requests: [], trang: query.trang, kichThuocTrang: 20, tongSo: 125,
  } }));
});
it("uses an explicit state and server total, retaining the open snapshot across page changes", async () => {
  const wrapper = mount(Workspace, { global: { plugins: [pinia], stubs: { Dialog: { props: ['visible'], template: '<section v-if="visible"><slot /></section>' }, LiveSpaBookingDetail: { props: ['booking'], template: '<div data-testid="snapshot">{{ booking.maDon }}<button data-testid="changed" @click="$emit(\'changed\', {trangThai: \'CONFIRMED\'})">confirm</button></div>' } } } });
  await flushPromises();
  expect(api.getWorkspace).toHaveBeenCalledWith(expect.objectContaining({ trangThai: "PENDING", trang: 1 }));
  expect(wrapper.text()).toContain("125");
  await wrapper.get('[data-testid="open-101"]').trigger("click");
  await wrapper.get('[data-testid="next-page"]').trigger("click"); await flushPromises();
  expect(wrapper.get('[data-testid="snapshot"]').text()).toContain("101");
  await wrapper.get('[data-testid="changed"]').trigger("click"); await flushPromises();
  expect(api.getWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({ trangThai: "CONFIRMED", trang: 1 }));
});
