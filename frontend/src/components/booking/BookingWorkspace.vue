<template>
  <div class="space-y-6">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div class="flex flex-wrap items-end gap-3">
        <label class="label"
          >Ngày xem<input v-model="date" class="field" type="date"
        /></label>
        <div class="flex gap-2" aria-label="Chế độ lịch">
          <button
            class="btn"
            :aria-pressed="mode === 'list'"
            @click="mode = 'list'"
          >
            List</button
          ><button
            class="btn"
            :aria-pressed="mode === 'calendar'"
            @click="mode = 'calendar'"
          >
            Calendar
          </button>
        </div>
      </div>
      <div class="flex gap-2">
        <button class="btn" :disabled="busy" @click="load">Tải lại</button
        ><button
          v-if="auth.can('BOOKING_MANAGE')"
          class="btn-accent"
          @click="creating = true"
        >
          Booking mới
        </button>
      </div>
    </div>
    <ActionFeedback :busy="busy" :error="error" :message="message" />
    <section v-if="pending.length && auth.can('BOOKING_MANAGE')" class="panel">
      <h2>Yêu cầu đơn mới ({{ pending.length }})</h2>
      <div class="mt-4 grid gap-4 md:grid-cols-2">
        <article
          v-for="b in pending"
          :key="b.id"
          class="rounded-xl border border-primary-100 p-4"
        >
          <p class="break-all font-bold">{{ b.trackingCode }}</p>
          <p class="muted">
            {{ b.khachHang.tenKhachHang }} · {{ b.chiTietDonList.length }} bé ·
            {{ dateLabel(b.date) }}
          </p>
          <div class="mt-3 flex flex-wrap gap-2">
            <button class="btn" @click="selectedId = b.id">Chi tiết</button
            >
            <BookingDecisionPanel :booking="b" @changed="load" />
          </div>
        </article>
      </div>
    </section>
    <div v-if="mode === 'list'" class="table-wrap">
      <table>
        <caption class="sr-only">
          Lịch hẹn ngày
          {{
            dateLabel(date)
          }}
        </caption>
        <thead>
          <tr>
            <th>Mã / Khách hàng</th>
            <th>Ca / Thú cưng</th>
            <th>Trạng thái</th>
            <th>Tạm tính</th>
            <th>Thao tác</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="b in daily" :key="b.id">
            <td>
              <p class="font-bold">{{ b.trackingCode }}</p>
              <p class="muted">{{ b.khachHang.tenKhachHang }}</p>
            </td>
            <td>
              {{ shiftName(b.shiftId) }}
              <p class="muted">
                {{
                  b.chiTietDonList
                    .filter((d) => !d.laDichVuThem)
                    .map((d) => d.thuCung.tenThuCung)
                    .join(", ")
                }}
              </p>
            </td>
            <td>
              <span class="badge">{{
                getBookingStatusBadge(b.trangThai).label
              }}</span>
            </td>
            <td>{{ formatCurrency(b.tongTienTamTinh) }}</td>
            <td>
              <button class="btn" @click="selectedId = b.id">
                Chi tiết đơn
              </button>
            </td>
          </tr>
          <tr v-if="!daily.length">
            <td colspan="5" class="muted">Chưa có lịch hẹn trong ngày này.</td>
          </tr>
        </tbody>
      </table>
    </div>
    <section
      v-else
      class="grid gap-3 sm:grid-cols-2 xl:grid-cols-7"
      aria-label="Lịch booking tuần"
    >
      <div v-for="day in week" :key="day" class="panel !p-3">
        <h3 class="mb-3 text-sm">{{ dateLabel(day) }}</h3>
        <button
          v-for="b in store.bookings.filter((b) => b.date === day)"
          :key="b.id"
          class="mb-2 block w-full rounded-xl border border-primary-200 bg-primary-50 p-3 text-left text-sm hover:bg-primary-100"
          @click="selectedId = b.id"
        >
          <strong>{{ b.khachHang.tenKhachHang }}</strong
          ><span class="muted block"
            >{{ shiftName(b.shiftId) }} ·
            {{ getBookingStatusBadge(b.trangThai).label }}</span
          >
        </button>
        <p v-if="!store.bookings.some((b) => b.date === day)" class="muted">
          Không có đơn
        </p>
      </div>
    </section>
    <section
      v-if="
        store.requests.filter((r) => r.status === 'PENDING').length &&
        auth.can('BOOKING_MANAGE')
      "
      class="panel"
    >
      <h2>Yêu cầu thêm dịch vụ</h2>
      <div
        v-for="r in store.requests.filter((r) => r.status === 'PENDING')"
        :key="r.id"
        class="mt-4 flex flex-wrap items-center justify-between gap-4 border-t border-primary-100 pt-4"
      >
        <div>
          <strong>{{ r.trackingCode }}</strong>
          <p class="mt-2 text-sm font-bold">
            {{ addonSummary(r).pet }} · {{ addonSummary(r).service }}
          </p>
          <p class="muted">
            Phí thêm tạm tính: {{ formatCurrency(addonSummary(r).amount) }}
          </p>
          <p class="muted">{{ r.note }}</p>
        </div>
        <div class="flex gap-2">
          <button
            class="btn-primary"
            :disabled="busy"
            @click="reviewRequest(r, 'APPROVED')"
          >
            Duyệt</button
          ><button
            class="btn-danger"
            :disabled="busy"
            @click="reviewRequest(r, 'REJECTED')"
          >
            Từ chối
          </button>
        </div>
      </div>
    </section>
    <Dialog
      v-model:visible="creating"
      modal
      header="Booking mới"
      :style="{ width: '72rem', maxWidth: '96vw' }"
      ><BookingForm internal @created="created" /></Dialog
    ><Dialog
      v-model:visible="detailVisible"
      modal
      header="Chi tiết đơn"
      :style="{ width: '60rem', maxWidth: '96vw' }"
      ><BookingDetailDialog v-if="selected" :booking="selected" @changed="load"
    /></Dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useRoute } from "vue-router";
import Dialog from "primevue/dialog";
import { useAuthStore } from "@/stores/auth";
import { useWorkspaceStore } from "@/stores/workspace";
import { bookingService } from "@/services/api/bookingService";
import { useAction } from "@/composables/useAction";
import { localDate, dateLabel, weekDays } from "@/utils/dates";
import { formatCurrency, getBookingStatusBadge } from "@/utils/formatters";
import { estimatePet } from "@/utils/pricing";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
import BookingForm from "./BookingForm.vue";
import BookingDetailDialog from "./BookingDetailDialog.vue";
import BookingDecisionPanel from "./BookingDecisionPanel.vue";
const store = useWorkspaceStore(),
  auth = useAuthStore(),
  route = useRoute(),
  date = ref(localDate()),
  mode = ref("list"),
  creating = ref(false),
  selectedId = ref(null),
  { busy, error, message, perform } = useAction();
const week = computed(() => weekDays(date.value || localDate())),
  daily = computed(() => store.bookings.filter((b) => b.date === date.value)),
  pending = computed(() =>
    store.bookings.filter((b) => b.trangThai === "PENDING"),
  ),
  selected = computed(() =>
    store.bookings.find((b) => b.id === selectedId.value),
  ),
  detailVisible = computed({
    get: () => !!selected.value,
    set: (v) => {
      if (!v) selectedId.value = null;
    },
  });
function shiftName(id) {
  return store.catalog.shifts.find((s) => s.id === id)?.tenCa || "—";
}
function addonSummary(request) {
  const booking = store.bookings.find((b) => b.id === request.bookingId);
  const detail = booking?.chiTietDonList.find((d) => d.id === request.detailId);
  const service = store.catalog.services.find(
    (s) => s.id === request.serviceId,
  );
  let amount = 0;
  if (detail && service) {
    amount = estimatePet(
      {
        ...detail.input,
        type: "SPA",
        comboId: null,
        serviceId: request.serviceId,
        weight: detail.canNangThucTe || detail.input.weight,
      },
      booking.date,
      store.catalog,
    ).amount;
  }
  return {
    pet: detail?.thuCung.tenThuCung || "Pet không còn tồn tại",
    service: service?.tenDichVu || "Dịch vụ không còn tồn tại",
    amount,
  };
}
async function load() {
  await perform(async () => {
    await store.loadBookings();
    await store.loadCatalog(date.value);
  });
}
onMounted(async () => {
  await load();
  if (route.query.code)
    selectedId.value =
      store.bookings.find((b) => b.trackingCode === route.query.code)?.id ||
      null;
});
async function created(b) {
  creating.value = false;
  date.value = b.date;
  await load();
  selectedId.value = b.id;
}
watch(() => route.query.code, async (code) => {
  if (!code) return;
  await load();
  selectedId.value = store.bookings.find((b) => b.trackingCode === code)?.id || null;
});
async function review(b, decision) {
  await perform(async () => {
    await bookingService.reviewBooking({ id: b.id, decision });
    await store.loadBookings();
  }, "Đã xử lý đơn.");
}
async function reviewRequest(r, decision) {
  await perform(async () => {
    await bookingService.reviewRequest({ id: r.id, decision });
    await store.loadBookings();
  }, "Đã xử lý yêu cầu.");
}
</script>
