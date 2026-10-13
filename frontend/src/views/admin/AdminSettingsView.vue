<template>
  <div class="space-y-6">
    <ActionFeedback :error="error" :busy="busy" :message="message" />
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
  { busy, error, message, perform } = useAction();
async function get() {
  const c = (await catalogService.catalog({ date: date.value })).data;
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
</script>
