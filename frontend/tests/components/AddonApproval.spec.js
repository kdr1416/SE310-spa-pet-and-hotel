// @vitest-environment jsdom
import { it, expect, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../../src/stores/auth";
import { useWorkspaceStore } from "../../src/stores/workspace";
import Workspace from "../../src/components/booking/BookingWorkspace.vue";
import { localDate } from "../../src/utils/dates";
vi.mock("vue-router", () => ({ useRoute: () => ({ query: {} }) }));
it("shows the pet, selected service and fee before approving an addon", async () => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const auth = useAuthStore();
  auth.user = {
    id: 2,
    vaiTro: "STAFF",
    workGroup: "RECEPTION",
    isActive: true,
  };
  const store = useWorkspaceStore();
  store.bookings = [
    {
      id: "b",
      trackingCode: "TRK-DEMO",
      date: localDate(),
      shiftId: 1,
      trangThai: "CONFIRMED",
      khachHang: { tenKhachHang: "Demo" },
      tongTienTamTinh: 100000,
      chiTietDonList: [
        {
          id: "d",
          thuCung: { tenThuCung: "Milo" },
          input: { weight: 5 },
          canNangThucTe: 5,
        },
      ],
    },
  ];
  store.requests = [
    {
      id: "r",
      bookingId: "b",
      detailId: "d",
      serviceId: 3,
      trackingCode: "TRK-DEMO",
      type: "ADDON",
      status: "PENDING",
      note: "Thêm giúp tôi",
    },
  ];
  store.catalog.services = [
    {
      id: 3,
      tenDichVu: "Vệ sinh tai móng",
      giaCoBan: 80000,
      isActive: true,
      loaiDichVu: "SPA_BASIC",
    },
  ];
  vi.spyOn(store, "loadBookings").mockResolvedValue();
  vi.spyOn(store, "loadCatalog").mockResolvedValue();
  const w = mount(Workspace, {
    global: {
      plugins: [pinia],
      stubs: { Dialog: true, BookingForm: true, BookingDetailDialog: true },
    },
  });
  await flushPromises();
  const queue = w
    .findAll("section")
    .find((s) => s.text().includes("Yêu cầu thêm dịch vụ"));
  expect(queue.text()).toContain("Milo");
  expect(queue.text()).toContain("Vệ sinh tai móng");
  expect(queue.text()).toMatch(/104\.000/);
});
