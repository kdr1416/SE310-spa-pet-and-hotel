// @vitest-environment jsdom
import { createPinia, setActivePinia } from "pinia";
import { mount } from "@vue/test-utils";
import { afterEach, expect, it, vi } from "vitest";
import router from "../../src/router";
import AppLayout from "../../src/layouts/AppLayout.vue";
import { useAuthStore } from "../../src/stores/auth";
afterEach(() => vi.restoreAllMocks());

it("sends care-only Staff to care tasks and hides inaccessible workspace links", async () => {
  const pinia = createPinia(); setActivePinia(pinia);
  const auth = useAuthStore();
  auth.token = "test-access-token";
  auth.user = { maNhanVien: 72, vaiTro: "STAFF", tenNhanVien: "Spa", isActive: true, dangHoatDong: true, quyen: ["CARE_UPDATE"] };
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  vi.spyOn(auth, "initAuth").mockResolvedValue();
  // Guest redirect must never choose overview, which this actor cannot read.
  await router.push("/login");
  expect(router.currentRoute.value.path).toBe("/staff/care-tasks");
  const wrapper = mount(AppLayout, { global: { plugins: [pinia, router], stubs: { RouterView: true, BookingNotifications: true } } });
  expect(wrapper.find('a[href="/staff/bookings"]').exists()).toBe(false);
  expect(wrapper.find('a[href="/staff/overview"]').exists()).toBe(false);
  expect(wrapper.find('a[href="/staff/care-tasks"]').exists()).toBe(true);
  wrapper.unmount();
});
