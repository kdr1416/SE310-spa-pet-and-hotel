// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { reactive } from "vue";
import { beforeEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ getWorkspace: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
let testRoute;
vi.mock("vue-router", () => ({ useRoute: () => testRoute, useRouter: () => ({ replace: async location => { testRoute.query = { ...location.query }; } }) }));
import { useAuthStore } from "../../src/stores/auth";
import Workspace from "../../src/components/booking/LiveBookingWorkspace.vue";
let pinia;
beforeEach(() => {
  vi.resetAllMocks(); testRoute = reactive({ query: {} });
  pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["BOOKING_APPROVE", "CHECKIN_PERFORM"] };
  api.getWorkspace.mockImplementation(async query => ({ success: true, data: {
    bookings: [{ id: query.trang === 2 ? 102 : 101, maDon: query.trang === 2 ? 102 : 101, trackingCode: "ABCD1234", trangThai: query.trangThai, thuCung: [], date: "2035-01-01" }], users: [], requests: [], trang: query.trang, kichThuocTrang: 20, tongSo: 125,
  } }));
});
it("resolves notification query changes across pending pages", async () => {
  const wrapper = mount(Workspace, { global: { plugins: [pinia], stubs: { Dialog: { props: ['visible'], template: '<section v-if="visible"><slot /></section>' }, LiveSpaBookingDetail: { props: ['booking'], template: '<div data-testid="snapshot">{{ booking.maDon }}</div>' } } } });
  await flushPromises();
  api.getWorkspace.mockImplementation(async query => ({ success: true, data: { bookings: [{ maDon: query.trang === 2 ? 202 : 101, trackingCode: query.trang === 2 ? "TARGET42" : "OTHER001", trangThai: "PENDING", date: "2035-01-01", thuCung: [] }], trang: query.trang, kichThuocTrang: query.kichThuocTrang, tongSo: 101 } }));
  testRoute.query.code = "TARGET42"; await flushPromises();
  expect(wrapper.get('[data-testid="snapshot"]').text()).toBe("202"); wrapper.unmount();
});
it("closes a final-handover snapshot when its authoritative completed row is outside the page", async () => {
  const wrapper = mount(Workspace, { global: { plugins: [pinia], stubs: { Dialog: { props: ['visible'], template: '<section v-if="visible"><slot /></section>' }, LiveSpaBookingDetail: { props: ['booking'], template: '<button data-testid="finish" @click="$emit(\'changed\', {trangThai: \'COMPLETED\', reloadOnly: true, pet: {maChiTietDon: 301, trangThai: \'DONE\'}})">finish</button>' } } } });
  await flushPromises(); await wrapper.get('[data-testid="open-101"]').trigger("click");
  api.getWorkspace.mockResolvedValueOnce({ success: true, data: { bookings: [], trang: 1, kichThuocTrang: 20, tongSo: 125 } });
  await wrapper.get('[data-testid="finish"]').trigger("click"); await flushPromises();
  expect(wrapper.find('[data-testid="finish"]').exists()).toBe(false);
  expect(wrapper.text()).toContain("không nằm trong trang hiện tại"); wrapper.unmount();
});
it("initializes the listing for a direct notification URL and consumes its query when closed", async () => {
  testRoute.query.code = "ABCD1234";
  const wrapper = mount(Workspace, { global: { plugins: [pinia], stubs: { Dialog: { props: ['visible'], template: '<section v-if="visible"><button data-testid="close-detail" @click="$emit(\'update:visible\', false)">close</button><slot /></section>' }, LiveSpaBookingDetail: { props: ['booking'], template: '<div data-testid="snapshot">{{ booking.maDon }}</div>' } } } });
  await flushPromises();
  expect(api.getWorkspace).toHaveBeenCalledWith(expect.objectContaining({ kichThuocTrang: 20, trangThai: "PENDING" }));
  await wrapper.get('[data-testid="close-detail"]').trigger("click"); await flushPromises(); expect(testRoute.query.code).toBeUndefined();
  testRoute.query.code = "ABCD1234"; await flushPromises(); expect(wrapper.find('[data-testid="snapshot"]').exists()).toBe(true); wrapper.unmount();
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
