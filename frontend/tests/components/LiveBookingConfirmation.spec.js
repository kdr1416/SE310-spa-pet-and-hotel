// @vitest-environment jsdom
import { mount } from "@vue/test-utils";
import { expect, it, vi } from "vitest";
vi.mock("../../src/services/api/httpClient", () => ({ IS_MOCK_ENABLED: false }));
vi.mock("../../src/components/booking/BookingForm.vue", () => ({ default: { template: '<button @click="$emit(\'created\', {trackingCode: \'ABCD1234\', emailDelivery: \'FAILED\'})">Đặt</button>' } }));
import View from "../../src/views/customer/CustomerBookingView.vue";
it("preserves live tracking and states email failure without generating a mock payment QR", async () => {
  const wrapper = mount(View, { global: { stubs: { RouterLink: { template: '<a><slot /></a>' }, PaymentQrPanel: { template: '<div data-testid="mock-qr" />' } } } });
  await wrapper.get("button").trigger("click");
  expect(wrapper.text()).toContain("ABCD1234");
  expect(wrapper.text()).toContain("chưa gửi được email");
  expect(wrapper.find('[data-testid="mock-qr"]').exists()).toBe(false);
});
