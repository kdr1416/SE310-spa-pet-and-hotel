// @vitest-environment jsdom
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, expect, it, vi } from "vitest";
vi.mock("vue-router", () => ({ useRouter: () => ({ replace: vi.fn() }) }));
afterEach(() => { vi.unstubAllEnvs(); vi.useRealTimers(); localStorage.clear(); });
it("invalidates private rows synchronously on a cross-tab session change before adopting the new actor", async () => {
  vi.resetModules(); vi.stubEnv("VITE_USE_MOCK_API", "false"); vi.useFakeTimers();
  const { useAuthStore } = await import("../../src/stores/auth");
  const { useWorkspaceStore } = await import("../../src/stores/workspace");
  const App = (await import("../../src/App.vue")).default;
  const pinia = createPinia(); setActivePinia(pinia);
  const auth = useAuthStore(); auth.user = { maNhanVien: 72, vaiTro: "STAFF", isActive: true, quyen: ["CARE_UPDATE"] }; auth.token = "test-token-a";
  const workspace = useWorkspaceStore(); workspace.careTasks = [{ maChiTietDon: 301 }];
  const wrapper = mount(App, { global: { plugins: [pinia], stubs: { RouterView: true } } });
  localStorage.setItem("petspa_auth_token", "test-token-b");
  localStorage.setItem("petspa_auth_user", JSON.stringify({ maNhanVien: 73, vaiTro: "STAFF", isActive: true, quyen: ["CARE_UPDATE"] }));
  window.dispatchEvent(new StorageEvent("storage", { key: "petspa_auth_user" }));
  expect(workspace.careTasks).toEqual([]); expect(auth.user).toBeNull();
  await vi.advanceTimersByTimeAsync(0); await flushPromises(); expect(auth.user.maNhanVien).toBe(73);
  wrapper.unmount();
});
