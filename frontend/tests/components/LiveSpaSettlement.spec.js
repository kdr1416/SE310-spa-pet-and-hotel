// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, expect, it, vi } from "vitest";
const api = vi.hoisted(() => ({ createInvoice: vi.fn(), getInvoice: vi.fn(), checkoutInvoice: vi.fn(), handoverPet: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: api }));
import { useAuthStore } from "../../src/stores/auth";
import Invoice from "../../src/components/booking/LiveSpaInvoice.vue";
let pinia;
const booking = () => ({ maDon: 101, trangThai: "IN_PROGRESS", thuCung: [301, 302].map(id => ({ maChiTietDon: id, tenThuCung: `Pet ${id}`, trangThai: "READY", dichVu: [{ maDichVu: 8 }], nhatKy: [] })) });
const invoice = (state = "UNPAID") => ({ maHoaDon: 902, maDon: 101, trangThai: state, conPhaiTra: 150000, tongTien: 150000, cocKhauTru: 0, dongTien: [{ maDongHoaDon: 1, tenKhoan: "Tắm", thanhTien: 150000 }], qr: null });
beforeEach(() => {
  vi.resetAllMocks(); pinia = createPinia(); setActivePinia(pinia);
  useAuthStore().user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["INVOICE_CREATE", "INVOICE_COLLECT", "PET_HANDOVER"] };
  api.createInvoice.mockResolvedValue({ success: true, data: invoice() }); api.getInvoice.mockResolvedValue({ success: true, data: invoice() });
  api.checkoutInvoice.mockResolvedValue({ success: true, data: invoice("PAID") });
  api.handoverPet.mockImplementation(async fields => ({ success: true, data: { ...booking().thuCung.find(pet => pet.maChiTietDon === fields.maChiTietDon), trangThai: "DONE" } }));
});
async function create() { const wrapper = mount(Invoice, { props: { booking: booking() }, global: { plugins: [pinia] } }); await wrapper.get('[data-testid="create-invoice"]').trigger("click"); await flushPromises(); return wrapper; }
it("creates for READY pets inside IN_PROGRESS booking, uses invoice ID and never auto hands over", async () => {
  const wrapper = await create();
  expect(api.createInvoice).toHaveBeenCalledWith(expect.objectContaining({ bookingId: 101 }));
  expect(wrapper.text()).toContain("Chưa có QR"); expect(wrapper.text()).toContain("Tắm");
  expect(wrapper.get('[data-testid="received"]').element.value).toBe("");
  await wrapper.get('[data-testid="received"]').setValue("200000"); await wrapper.get('[data-testid="change"]').setValue("50000");
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  expect(api.checkoutInvoice).toHaveBeenCalledWith(expect.objectContaining({ invoiceId: 902, soTienThucNhan: 200000, soTienDaTraLai: 50000, phuongThuc: "CASH", requestId: expect.any(String) }));
  expect(api.handoverPet).not.toHaveBeenCalled();
  expect(wrapper.get('[data-testid="handover-consent-301"]').element.checked).toBe(false);
  await wrapper.get('[data-testid="handover-form-301"]').trigger("submit"); await flushPromises(); expect(api.handoverPet).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="handover-consent-301"]').setValue(true);
  await wrapper.get('[data-testid="handover-form-301"]').trigger("submit"); await flushPromises();
  expect(api.handoverPet).toHaveBeenCalledWith(expect.objectContaining({ maChiTietDon: 301, xacNhanDaNhan: true }));
  expect(wrapper.emitted("changed").at(-1)[0].trangThai).toBe("IN_PROGRESS");
  await wrapper.get('[data-testid="handover-consent-302"]').setValue(true);
  await wrapper.get('[data-testid="handover-form-302"]').trigger("submit"); await flushPromises();
  expect(api.handoverPet).toHaveBeenCalledTimes(2);
  expect(wrapper.emitted("changed").at(-1)[0]).toMatchObject({ trangThai: "COMPLETED", reloadOnly: true });
});
it("keeps entered money, invoice snapshot and key after errors, with explicit manual retry", async () => {
  const wrapper = await create();
  api.getInvoice.mockRejectedValueOnce({ message: "Không tải được" }); await wrapper.get('[data-testid="reload-invoice"]').trigger("click"); await flushPromises();
  expect(wrapper.text()).toContain("Tắm");
  await wrapper.get('[data-testid="received"]').setValue("150000"); await wrapper.get('[data-testid="change"]').setValue("0");
  api.checkoutInvoice.mockRejectedValueOnce({ message: "Mất kết nối" });
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  const first = api.checkoutInvoice.mock.calls[0][0];
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  expect(api.checkoutInvoice.mock.calls[1][0]).toEqual(first);
  expect(api.createInvoice).toHaveBeenCalledTimes(1);
});
it("blocks payment during E24 and refreshes a rejected amount without losing entered money", async () => {
  const wrapper = await create(); let resolveRead;
  api.getInvoice.mockImplementationOnce(() => new Promise(resolve => { resolveRead = resolve; }));
  await wrapper.get('[data-testid="received"]').setValue("150000"); await wrapper.get('[data-testid="change"]').setValue("0");
  await wrapper.get('[data-testid="reload-invoice"]').trigger("click");
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  expect(api.checkoutInvoice).not.toHaveBeenCalled();
  resolveRead({ success: true, data: invoice() }); await flushPromises();
  api.checkoutInvoice.mockRejectedValueOnce({ errorCode: "AMOUNT_MISMATCH", message: "Số tiền đã thay đổi" });
  await wrapper.get('[data-testid="payment-form"]').trigger("submit"); await flushPromises();
  expect(api.getInvoice).toHaveBeenCalledTimes(2);
  expect(wrapper.get('[data-testid="received"]').element.value).toBe("150000");
});
it("permits replacing a revoked invoice, and restricts E24 to the read grant", async () => {
  const b = booking(); b.hoaDon = invoice("REVOKED");
  const wrapper = mount(Invoice, { props: { booking: b }, global: { plugins: [pinia] } });
  expect(wrapper.find('[data-testid="create-invoice"]').exists()).toBe(true);
  useAuthStore().user.quyen = ["INVOICE_COLLECT"]; await flushPromises();
  expect(wrapper.find('[data-testid="reload-invoice"]').exists()).toBe(false);
});
