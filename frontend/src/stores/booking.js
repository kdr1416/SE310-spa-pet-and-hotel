import { defineStore } from "pinia";
import { bookingService } from "@/services/api/bookingService";
export const useBookingStore = defineStore("booking", {
  state: () => ({ currentBooking: null, loading: false, error: null }),
  actions: {
    async fetchByTracking(trackingCode, phone) {
      this.loading = true;
      this.error = null;
      this.currentBooking = null;
      try {
        const { data } = await bookingService.lookupBooking({
          trackingCode,
          phone,
        });
        this.currentBooking = data;
        return { success: true, data };
      } catch (e) {
        this.error = e.message;
        return { success: false, message: e.message };
      } finally {
        this.loading = false;
      }
    },
  },
});
