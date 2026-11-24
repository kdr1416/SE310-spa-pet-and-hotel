import { defineStore } from "pinia";
import { reactive, toRefs, watch } from "vue";
import { useAuthStore } from "./auth";
import { bookingService } from "@/services/api/bookingService";
import { catalogService } from "@/services/api/catalogService";
export const useWorkspaceStore = defineStore("workspace", () => {
  const auth = useAuthStore();
  const state = reactive({
    scopeKey: "", scopeEpoch: 0,
    bookings: [],
    bookingsQuery: { trang: 1, kichThuocTrang: 20, trangThai: "PENDING" },
    bookingsPage: { trang: 1, kichThuocTrang: 20, tongSo: 0 },
    careTasks: [],
    careQuery: { trang: 1, kichThuocTrang: 20 },
    carePage: { trang: 1, kichThuocTrang: 20, tongSo: 0 },
    bookingsSequence: 0,
    careSequence: 0,
    users: [],
    requests: [],
    catalog: {
      services: [],
      combos: [],
      rooms: [],
      shifts: [],
      availability: [],
      surchargeRules: [],
    },
  });
  function resetPrivate(resetQueries = false) {
    state.bookingsSequence++; state.careSequence++; state.scopeEpoch++;
    state.bookings = []; state.users = []; state.requests = []; state.careTasks = [];
    state.bookingsPage = { trang: 1, kichThuocTrang: 20, tongSo: 0 };
    state.carePage = { trang: 1, kichThuocTrang: 20, tongSo: 0 };
    if (resetQueries) {
      state.bookingsQuery = { trang: 1, kichThuocTrang: 20, trangThai: "PENDING" };
      state.careQuery = { trang: 1, kichThuocTrang: 20 };
    }
  }
  // Pinia outlives routed views; private rows and pending reads belong to one principal and grant set.
  watch(() => JSON.stringify(auth.user && {
    id: auth.user.maNhanVien ?? auth.user.id, role: auth.user.vaiTro,
    active: auth.user.dangHoatDong ?? auth.user.isActive, group: auth.user.workGroup,
    grants: [...(auth.user.quyen ?? [])].sort(),
  }), key => { state.scopeKey = key; resetPrivate(true); }, { immediate: true, flush: "sync" });
  async function readProtected(send) {
    const epoch = state.scopeEpoch;
    function denied(error) {
      if (epoch === state.scopeEpoch && ["FORBIDDEN", "UNAUTHENTICATED"].includes(error?.errorCode)) resetPrivate();
    }
    try { const response = await send(); if (response.success === false) denied(response); return response; }
    catch (error) { denied(error); throw error; }
  }
  const actions = {
    async loadBookings(query = {}) {
      const changed = ["trangThai", "tuNgay", "denNgay", "kichThuocTrang"].some(key => key in query && query[key] !== this.bookingsQuery[key]);
      this.bookingsQuery = { ...this.bookingsQuery, ...query, ...(changed ? { trang: 1 } : {}) };
      const sequence = ++this.bookingsSequence;
      const response = await readProtected(() => bookingService.getWorkspace({ ...this.bookingsQuery }));
      if (sequence !== this.bookingsSequence || response.success === false) return response;
      const { data } = response;
      this.bookings = data.bookings;
      this.users = data.users ?? [];
      this.requests = data.requests ?? [];
      this.bookingsPage = { trang: data.trang ?? 1, kichThuocTrang: data.kichThuocTrang ?? data.bookings.length, tongSo: data.tongSo ?? data.bookings.length };
      return response;
    },
    async loadCareTasks(query = {}) {
      const changed = ["trangThai", "tuNgay", "denNgay", "kichThuocTrang"].some(key => key in query && query[key] !== this.careQuery[key]);
      this.careQuery = { ...this.careQuery, ...query, ...(changed ? { trang: 1 } : {}) };
      const sequence = ++this.careSequence;
      const response = await readProtected(() => bookingService.listCareTasks({ ...this.careQuery }));
      if (sequence !== this.careSequence || response.success === false) return response;
      this.careTasks = response.data.items;
      const { trang, kichThuocTrang, tongSo } = response.data;
      this.carePage = { trang, kichThuocTrang, tongSo };
      return response;
    },
    async loadCatalog(date) {
      const { data } = await catalogService.catalog({ date });
      this.catalog = data;
    },
  };
  return { ...toRefs(state), ...actions, resetPrivate };
});
