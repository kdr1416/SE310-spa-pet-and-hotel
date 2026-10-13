// @vitest-environment jsdom
import { it, expect, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../../src/stores/auth";
vi.mock("../../src/services/mock/mockRepository", () => ({
  mockCall: async () => ({ id: 9, vaiTro: "UNKNOWN", isActive: true }),
  transaction: vi.fn(),
}));
it("does not treat an unsupported role as authenticated (avoids redirect loops)", async () => {
  setActivePinia(createPinia());
  sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 9 }));
  const auth = useAuthStore();
  await auth.initAuth();
  expect(auth.isAuthenticated).toBe(false);
});
