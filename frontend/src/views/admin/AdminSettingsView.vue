<template>
  <div class="space-y-6">
    <ActionFeedback :error="error" :busy="busy" :message="message" />
    <section class="panel space-y-4">
      <h2>Xác minh giấy tiêm chủng khi check-in</h2>
      <p class="muted">Mặc định không bắt buộc. Khách luôn đặt được khi chưa tải ảnh; khi bật, nhân viên phải kiểm tra ảnh hoặc giấy mang tới quầy trước tiếp nhận.</p>
      <form class="space-y-4" @submit.prevent="savePolicy">
        <label v-for="(label, type) in serviceTypes" :key="type" class="flex items-center gap-3">
          <input v-model="vaccinePolicy[type]" type="checkbox" />Yêu cầu xác minh với {{ label }}
        </label>
        <button class="btn-primary" :disabled="busy">Lưu cấu hình vaccine</button>
      </form>
    </section>
    <section class="panel space-y-4">
      <div class="flex flex-wrap items-center justify-between gap-3"><h2>Hộp thư gửi lại mã (mock)</h2><button class="btn" :disabled="busy" @click="loadOutbox">Tải thư mô phỏng</button></div>
      <p class="muted">Chỉ Admin xem. Không có thư nào được gửi SMTP thật.</p>
      <p v-if="!outbox.length" class="muted">Chưa tải thư hoặc chưa có yêu cầu phù hợp.</p>
      <article v-for="mail in outbox" :key="mail.id" class="rounded-xl border border-primary-100 p-3">
        <strong>{{ mail.to }}</strong><p class="mt-1 text-sm">{{ mail.trackingCodes.join(', ') }}</p>
      </article>
    </section>
    <section class="panel">
      <h2>Giờ hoạt động / ca</h2>
      <p class="muted mt-2">
        Giữ giờ của ca đã có booking đang mở hoặc nằm trong lịch published.
      </p>
      <form
        v-for="s in shifts"
        :key="s.id"
        class="mt-5 grid items-end gap-4 sm:grid-cols-4"
        @submit.prevent="saveShift(s)"
      >
        <h3>{{ s.tenCa }}</h3>
        <label class="label"
          >Bắt đầu<input
            v-model="hours[s.id].start"
            class="field"
            type="time"
            required /></label
        ><label class="label"
          >Kết thúc<input
            v-model="hours[s.id].end"
            class="field"
            type="time"
            required /></label
        ><button class="btn-primary" :disabled="busy">Lưu giờ</button>
      </form>
    </section>
    <section class="panel">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <h2>Sức chứa theo ngày</h2>
        <label class="label"
          >Ngày<input v-model="date" class="field" type="date" :min="today"
        /></label>
      </div>
      <div class="mt-5 grid gap-4 md:grid-cols-3">
        <form
          v-for="s in availability"
          :key="s.id"
          class="rounded-xl border border-primary-100 p-4"
          @submit.prevent="saveCapacity(s)"
        >
          <h3>{{ s.tenCa }}</h3>
          <p class="muted my-3">
            Đã giữ: {{ s.reserved }} · Còn {{ s.left }} chỗ
          </p>
          <label class="label"
            >Tổng sức chứa<input
              v-model.number="totals[s.id]"
              class="field"
              type="number"
              :min="s.reserved"
              step="1"
              required /></label
          ><button class="btn-primary mt-4" :disabled="busy">Lưu slot</button>
        </form>
      </div>
    </section>
  </div>
</template>
<script setup>
import { ref, reactive, onMounted, watch } from "vue";
import { staffService } from "@/services/api/staffService";
import { catalogService } from "@/services/api/catalogService";
import { localDate } from "@/utils/dates";
import { useAction } from "@/composables/useAction";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const date = ref(localDate()),
  today = localDate(),
  shifts = ref([]),
  availability = ref([]),
  hours = reactive({}),
  totals = reactive({}),
  vaccinePolicy = reactive({ SPA: false, HOTEL: false, COMBO_SPA_HOTEL: false }),
  outbox = ref([]),
  { busy, error, message, perform } = useAction();
async function get() {
  const c = (await catalogService.catalog({ date: date.value })).data;
  Object.assign(vaccinePolicy, c.vaccinePolicy || {});
  shifts.value = c.shifts;
  availability.value = c.availability;
  for (const s of c.shifts)
    hours[s.id] = { start: s.gioBatDau, end: s.gioKetThuc };
  for (const s of c.availability) totals[s.id] = s.total;
}
async function load() {
  await perform(get);
}
onMounted(load);
watch(date, load);
async function saveShift(s) {
  await perform(async () => {
    await staffService.updateShift({ id: s.id, ...hours[s.id] });
    await get();
  }, "Đã lưu giờ.");
}
async function saveCapacity(s) {
  await perform(async () => {
    await staffService.updateCapacity({
      date: date.value,
      shiftId: s.id,
      total: totals[s.id],
    });
    await get();
  }, "Đã lưu sức chứa.");
}
const serviceTypes = { SPA: 'Spa', HOTEL: 'Hotel', COMBO_SPA_HOTEL: 'Spa + Hotel' };
async function savePolicy() {
  await perform(async () => { await staffService.updateVaccinePolicy({ policy: { ...vaccinePolicy } }); await get(); }, 'Đã lưu cấu hình xác minh tại check-in.');
}
async function loadOutbox() {
  await perform(async () => { outbox.value = (await staffService.getMailOutbox()).data.slice().reverse(); });
}
</script>
