import { defineStore } from "pinia";
import { bookingService } from "@/services/api/bookingService";
import { catalogService } from "@/services/api/catalogService";
export const useWorkspaceStore = defineStore("workspace", {
  state: () => ({
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
  }),
  actions: {
    async loadBookings(query = {}) {
      const changed = ["trangThai", "tuNgay", "denNgay", "kichThuocTrang"].some(key => key in query && query[key] !== this.bookingsQuery[key]);
      this.bookingsQuery = { ...this.bookingsQuery, ...query, ...(changed ? { trang: 1 } : {}) };
      const sequence = ++this.bookingsSequence;
      const response = await bookingService.getWorkspace({ ...this.bookingsQuery });
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
      const response = await bookingService.listCareTasks({ ...this.careQuery });
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
  },
});
