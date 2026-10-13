import { defineStore } from "pinia";
import { bookingService } from "@/services/api/bookingService";
import { catalogService } from "@/services/api/catalogService";
export const useWorkspaceStore = defineStore("workspace", {
  state: () => ({
    bookings: [],
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
    async loadBookings() {
      const { data } = await bookingService.getWorkspace();
      Object.assign(this, data);
    },
    async loadCatalog(date) {
      const { data } = await catalogService.catalog({ date });
      this.catalog = data;
    },
  },
});
