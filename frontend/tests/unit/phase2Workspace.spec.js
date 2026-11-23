// @vitest-environment jsdom
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, expect, it, vi } from "vitest";
const services = vi.hoisted(() => ({ getWorkspace: vi.fn(), listCareTasks: vi.fn() }));
vi.mock("../../src/services/api/bookingService", () => ({ bookingService: services }));
import { useWorkspaceStore } from "../../src/stores/workspace";
import { useAuthStore } from "../../src/stores/auth";
beforeEach(() => { setActivePinia(createPinia()); vi.resetAllMocks(); });

it("keeps server pagination and ignores older booking responses", async () => {
  const store = useWorkspaceStore();
  let resolveOld;
  services.getWorkspace.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
    .mockResolvedValueOnce({ success: true, data: { bookings: [{ id: 9 }], users: [], requests: [], trang: 1, kichThuocTrang: 20, tongSo: 125 } });
  const old = store.loadBookings({ trang: 3 });
  await store.loadBookings({ trangThai: "CONFIRMED" });
  resolveOld({ success: true, data: { bookings: [{ id: 1 }], trang: 3, kichThuocTrang: 20, tongSo: 1 } });
  await old;
  expect(store.bookings).toEqual([{ id: 9 }]);
  expect(store.bookingsQuery).toMatchObject({ trang: 1, trangThai: "CONFIRMED" });
  expect(store.bookingsPage).toMatchObject({ tongSo: 125, trang: 1 });
  expect(services.getWorkspace).toHaveBeenLastCalledWith(expect.objectContaining({ trang: 1, trangThai: "CONFIRMED" }));
});

it("loads care separately and ignores stale care pages", async () => {
  const store = useWorkspaceStore();
  let resolveOld;
  services.listCareTasks.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
    .mockResolvedValueOnce({ success: true, data: { items: [{ maChiTietDon: 30 }], trang: 1, kichThuocTrang: 20, tongSo: 101 } });
  const old = store.loadCareTasks({ trang: 2 });
  await store.loadCareTasks({ trangThai: "READY" });
  resolveOld({ success: true, data: { items: [], trang: 2, kichThuocTrang: 20, tongSo: 0 } });
  await old;
  expect(store.careTasks).toEqual([{ maChiTietDon: 30 }]);
  expect(store.carePage.tongSo).toBe(101);
  expect(services.getWorkspace).not.toHaveBeenCalled();
});
it("clears private state immediately on principal/grant change and ignores an old principal's response", async () => {
  const auth = useAuthStore(); auth.user = { maNhanVien: 1, vaiTro: "STAFF", quyen: ["CARE_UPDATE"] };
  const store = useWorkspaceStore();
  services.listCareTasks.mockResolvedValueOnce({ success: true, data: { items: [{ maChiTietDon: 301, nhatKy: [{ anhUrl: "https://private.example.test/signed" }] }], trang: 1, kichThuocTrang: 20, tongSo: 1 } });
  await store.loadCareTasks(); store.bookings = [{ maDon: 101 }];
  let resolveOld;
  services.listCareTasks.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
  const pending = store.loadCareTasks();
  auth.user = { maNhanVien: 2, vaiTro: "STAFF", quyen: ["CARE_UPDATE"] };
  expect(store.careTasks).toEqual([]); expect(store.bookings).toEqual([]);
  resolveOld({ success: true, data: { items: [{ maChiTietDon: 301 }], trang: 1, kichThuocTrang: 20, tongSo: 1 } }); await pending;
  expect(store.careTasks).toEqual([]);
  store.careTasks = [{ maChiTietDon: 302 }]; auth.user.quyen = [];
  expect(store.careTasks).toEqual([]);
});
it("discards cached private pages on forbidden refresh", async () => {
  const store = useWorkspaceStore(); store.careTasks = [{ maChiTietDon: 301 }]; store.bookings = [{ maDon: 101 }];
  services.listCareTasks.mockRejectedValueOnce({ errorCode: "FORBIDDEN" });
  await expect(store.loadCareTasks()).rejects.toMatchObject({ errorCode: "FORBIDDEN" }); expect(store.careTasks).toEqual([]);
  services.getWorkspace.mockRejectedValueOnce({ errorCode: "FORBIDDEN" });
  await expect(store.loadBookings()).rejects.toMatchObject({ errorCode: "FORBIDDEN" }); expect(store.bookings).toEqual([]);
});
