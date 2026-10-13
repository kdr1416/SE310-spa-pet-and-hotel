// @vitest-environment jsdom
import { it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia } from "pinia";
import { useAuthStore } from "../../src/stores/auth";
import Overview from "../../src/components/dashboard/OverviewWorkspace.vue";
const api = vi.hoisted(() => ({ workspace: vi.fn(), finance: vi.fn(), schedule: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: { getWorkspace: api.workspace } }));
vi.mock("../../src/services/api/financeService", () => ({ financeService: { getFinance: api.finance } }));
vi.mock("../../src/services/api/scheduleService", () => ({ scheduleService: { getSchedule: api.schedule } }));
beforeEach(() => {
  vi.clearAllMocks();
  api.workspace.mockResolvedValue({ data: { bookings: [] } });
  api.finance.mockResolvedValue({ data: { ledger: [], requests: [] } });
  api.schedule.mockResolvedValue({ data: { assignments: [], periods: [], shifts: [] } });
});
function render(user) {
  const pinia = createPinia();
  useAuthStore(pinia).user = user;
  return mount(Overview, { global: { plugins: [pinia], stubs: { RouterLink: { props: ["to"], template: '<a :href="to"><slot /></a>' } } } });
}
it("never loads finance or shows financial widgets for Spa staff", async () => {
  const w = render({ id: 3, vaiTro: "STAFF", workGroup: "SPA" });
  await flushPromises();
  expect(api.finance).not.toHaveBeenCalled();
  expect(w.text()).not.toContain("Thu ròng");
  expect(w.text()).not.toContain("Yêu cầu đang mở");
  expect(w.text()).toContain("Pet được phân công");
  w.unmount();
});
it("shows unavailable revenue rather than zero when finance fails", async () => {
  api.finance.mockRejectedValue(new Error("Không kết nối được"));
  const w = render({ id: 1, vaiTro: "ADMIN" });
  await flushPromises();
  expect(w.text()).toContain("Tài chính: Không kết nối được");
  const revenue = w.findAll("article").find(x => x.text().includes("Thu ròng"));
  expect(revenue.text()).toContain("—");
  expect(revenue.text()).not.toContain("0 ₫");
  w.unmount();
});
it("keeps permanent reviews out of the actionable request queue", async () => {
  api.finance.mockResolvedValue({ data: { ledger: [], requests: [
    { id: "review", type: "REVIEW", status: "PENDING", trackingCode: "REVIEW-ONLY" },
    { id: "addon", type: "ADDON", status: "PENDING", trackingCode: "ADDON-OPEN" },
  ] } });
  const w = render({ id: 1, vaiTro: "ADMIN" });
  await flushPromises();
  expect(w.text()).not.toContain("REVIEW-ONLY");
  expect(w.text()).toContain("ADDON-OPEN");
  expect(w.findAll("a").find(link => link.text().includes("ADDON-OPEN")).attributes("href")).toBe("/admin/bookings");
  w.unmount();
});
