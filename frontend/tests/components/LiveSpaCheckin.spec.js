// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, afterEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ getBookingOptions: vi.fn(), getFurConditions: vi.fn(), getAssignmentCandidates: vi.fn(), checkInPet: vi.fn(), reviewBooking: vi.fn() }));
vi.mock("../../src/services/api/catalogService", () => ({ catalogService: api }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
import { useAuthStore } from "../../src/stores/auth";
import Detail from "../../src/components/booking/LiveSpaBookingDetail.vue";
let pinia;
const booking = () => ({ maDon: 101, trackingCode: "ABCD1234", trangThai: "IN_PROGRESS", batDauDuKien: "2034-12-31T01:00:00Z", thuCung: [{ maChiTietDon: 301, tenThuCung: "Milo", trangThai: "NOT_ARRIVED", dichVu: [{ maDichVu: 8, tenDichVu: "Tắm" }], nhatKy: [] }] });
beforeEach(() => {
  vi.resetAllMocks(); vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2035-01-01T02:00:00Z"));
  pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["CHECKIN_PERFORM"] };
  api.getBookingOptions.mockResolvedValue({ success: true, data: { ca: [{ maCa: 7, tenCa: "Ca hiện tại", gioBatDau: "08:00", gioKetThuc: "12:00", maKhungGio: null, moNhanKhach: false }] } });
  api.getFurConditions.mockResolvedValue({ success: true, data: [{ ma: "NORMAL", ten: "Bình thường", dangHoatDong: true }] });
  api.getAssignmentCandidates.mockResolvedValue({ success: true, data: [{ maNhanVien: 44, tenNhanVien: "Spa A" }] });
  api.checkInPet.mockResolvedValue({ success: true, data: { ...booking().thuCung[0], trangThai: "WAITING" } });
});
afterEach(() => vi.useRealTimers());
async function open() { const wrapper = mount(Detail, { props: { booking: booking() }, global: { plugins: [pinia] } }); await flushPromises(); await wrapper.get('[data-testid="checkin-301"]').trigger("click"); await flushPromises(); return wrapper; }
async function fill(wrapper) {
  for (const [field, value] of Object.entries({ weight: "4", health: "Khỏe", fur: "NORMAL", employee: "44", price: "SIZE_THUC_TE" })) await wrapper.get(`[data-testid="${field}"]`).setValue(value);
  await wrapper.get('[data-testid="consent"]').setValue(true);
  const file = new File(["image"], "intake.jpg", { type: "image/jpeg" });
  const input = wrapper.get('[data-testid="intake-photo"]'); Object.defineProperty(input.element, "files", { value: [file] }); await input.trigger("change"); return file;
}
it("uses today's operating shift and explicit complete inputs to check in a second pet", async () => {
  const wrapper = await open();
  expect(api.getAssignmentCandidates).toHaveBeenCalledWith({ ngay: "2035-01-01", maCa: 7 });
  expect(wrapper.get('[data-testid="consent"]').element.checked).toBe(false);
  expect(wrapper.get('[data-testid="price"]').element.value).toBe("");
  const file = await fill(wrapper); await wrapper.get('[data-testid="checkin-form"]').trigger("submit"); await flushPromises();
  expect(api.checkInPet).toHaveBeenCalledWith(expect.objectContaining({ maChiTietDon: 301, maNhanVien: 44, tinhTrangLong: ["NORMAL"], ghiChuSucKhoe: "Khỏe", quyetDinhGia: "SIZE_THUC_TE", xacNhanKhach: true, anhTiepNhan: file, requestId: expect.any(String) }));
  expect(api.checkInPet.mock.calls[0][0].bookingId).toBeUndefined();
  expect(wrapper.emitted("changed")[0][0]).toMatchObject({ trangThai: "IN_PROGRESS" });
});
it("never invents consent, health or current assignee and retains inputs after field errors", async () => {
  const wrapper = await open(); await wrapper.get('[data-testid="checkin-form"]').trigger("submit"); await flushPromises();
  expect(api.checkInPet).not.toHaveBeenCalled();
  await fill(wrapper);
  api.checkInPet.mockRejectedValueOnce({ message: "Danh mục thay đổi", errorCode: "VALIDATION_FAILED", errors: [{ field: "tinhTrangLong", message: "Chọn lại tình trạng lông" }] });
  await wrapper.get('[data-testid="checkin-form"]').trigger("submit"); await flushPromises();
  expect(wrapper.get('[role="alert"]').text()).toContain("Chọn lại tình trạng lông");
  expect(wrapper.get('[data-testid="health"]').element.value).toBe("Khỏe");
});
