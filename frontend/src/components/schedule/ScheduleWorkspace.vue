<template>
  <div class="space-y-6">
    <ActionFeedback :busy="busy" :error="error" :message="message" />
    <section v-if="admin" class="panel space-y-4 no-print">
      <h2>Ngày nghỉ tiếp nhận trong tuần chưa mở đăng ký</h2>
      <p class="muted">Ngày nghỉ không nhận Spa hoặc nhận–trả Hotel. Các bé đang lưu trú vẫn được chăm sóc và nhân viên vẫn đăng ký ca trực Hotel.</p>
      <form class="space-y-4" @submit.prevent="saveClosedDays">
        <div class="grid gap-4 sm:grid-cols-2">
          <label class="label">Thứ Hai bắt đầu tuần<input v-model="holiday.week" class="field" type="date" :min="nextWeek" required /></label>
          <label class="label">Lý do<input v-model="holiday.reason" class="field" maxlength="200" /></label>
        </div>
        <p v-if="holidayLocked" class="text-sm text-amber-800">Tuần này đã mở đăng ký. Chọn một tuần chưa mở form để thiết lập ngày nghỉ.</p>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <label v-for="day in holidayDays" :key="day" class="flex items-center gap-2 rounded-xl border border-primary-100 p-3">
            <input v-model="holiday.dates" :value="day" type="checkbox" :disabled="holidayLocked" />{{ weekdayLabel(day) }}
          </label>
        </div>
        <button class="btn-primary" :disabled="busy || holidayLocked">Lưu ngày nghỉ</button>
      </form>
    </section>
    <section v-if="admin" class="panel no-print">
      <h2>Mở đợt đăng ký tuần kế tiếp</h2>
      <form
        class="mt-5 grid gap-4 md:grid-cols-4"
        @submit.prevent="createPeriod"
      >
        <label class="label"
          >Thứ Hai bắt đầu<input
            v-model="period.week"
            class="field"
            type="date"
            :min="nextWeek"
            required /></label
        ><label class="label"
          >Mở form<input
            v-model="period.openAt"
            class="field"
            type="datetime-local"
            required /></label
        ><label class="label"
          >Deadline<input
            v-model="period.deadline"
            class="field"
            type="datetime-local"
            required /></label
        ><label class="label"
          >Số nhân viên / ca<input
            v-model.number="period.capacity"
            class="field"
            type="number"
            min="1"
            max="100"
            required /></label
        ><button class="btn-primary justify-self-start" :disabled="busy">
          Tạo đợt
        </button>
      </form>
    </section>
    <section class="panel no-print">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <label class="label"
          >Tuần hiển thị<input v-model="viewDate" class="field" type="date"
        /></label>
        <div class="flex gap-2">
          <button class="btn" :disabled="busy" @click="load">Tải lại</button
          ><button v-if="admin" class="btn" @click="print">
            In / xuất PDF
          </button>
        </div>
      </div>
    </section>
    <section class="panel">
      <h2>
        Lịch {{ admin ? "nhân viên" : "của bạn" }} · {{ dateLabel(days[0]) }}–{{
          dateLabel(days[6])
        }}
      </h2>
      <p class="muted mt-2">
        {{
          admin
            ? "Lịch dự thảo được ghi rõ; chỉ ca đã duyệt thuộc đợt đã chốt mới hiển thị với Staff."
            : "Chỉ hiển thị các ca đã được Admin chốt."
        }}
      </p>
      <div class="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
        <article
          v-for="day in days"
          :key="day"
          class="rounded-xl border border-primary-100 p-3"
        >
          <h3 class="text-sm">{{ weekdayLabel(day) }}</h3>
          <p v-if="data.closedDays?.[day]" class="mt-2 text-xs font-bold text-amber-800">Nghỉ nhận–trả · vẫn trực Hotel</p>
          <div
            v-for="r in data.assignments.filter((r) => r.date === day)"
            :key="r.id"
            class="mt-3 rounded-lg bg-primary-50 p-2 text-sm"
          >
            <strong>{{ shift(r.shiftId)?.tenCa }}</strong>
            <p class="text-xs">{{ r.purpose === 'HOTEL_CARE' ? 'Chăm sóc Hotel' : 'Tiếp nhận / Spa' }}</p>
            <p>
              {{ shift(r.shiftId)?.gioBatDau }}–{{
                shift(r.shiftId)?.gioKetThuc
              }}
            </p>
            <p v-if="admin">{{ employee(r.employeeId)?.tenNhanVien }}</p>
            <span v-if="admin" class="text-xs">{{
              data.periods.find((p) => p.id === r.periodId)?.published
                ? "Đã chốt"
                : "Dự thảo"
            }}</span>
          </div>
          <p
            v-if="!data.assignments.some((r) => r.date === day)"
            class="muted mt-3"
          >
            Chưa có ca
          </p>
          <div v-if="!admin && available.some((p) => weekDays(p.week).includes(day))" class="mt-3 space-y-2 no-print">
            <button v-for="s in data.shifts.filter((s) => s.isActive)" :key="s.id" type="button" class="btn w-full text-xs" :disabled="busy || alreadyRegistered(day, s.id)" @click="selectShift(day, s.id)">
              {{ alreadyRegistered(day, s.id) ? 'Đã đăng ký' : 'Chọn' }} {{ s.tenCa }}{{ data.closedDays?.[day] ? ' · trực Hotel' : '' }}
            </button>
          </div>
        </article>
      </div>
    </section>
    <section class="panel no-print">
      <h2>{{ admin ? "Các đợt đăng ký" : "Đăng ký ca tuần tới" }}</h2>
      <div v-if="admin" class="mt-4 space-y-4">
        <article
          v-for="p in data.periods"
          :key="p.id"
          class="flex flex-wrap items-center justify-between gap-4 border-b border-primary-100 pb-4"
        >
          <div>
            <strong>Tuần {{ dateLabel(p.week) }}</strong>
            <p class="muted">
              {{ p.capacity }} người / ca · Deadline
              {{ formatDateTime(p.deadline) }} ·
              {{ p.published ? "Đã chốt" : p.open ? "Đang mở" : "Đang khóa" }}
            </p>
          </div>
          <div v-if="!p.published" class="flex gap-2">
            <button
              class="btn"
              :disabled="busy"
              @click="act('togglePeriod', { id: p.id })"
            >
              {{ p.open ? "Khóa form" : "Mở form" }}</button
            ><button
              class="btn-primary"
              :disabled="busy"
              @click="act('publishSchedule', { id: p.id })"
            >
              Chốt & công bố
            </button>
          </div>
        </article>
      </div>
      <form
        v-else
        class="mt-5 grid gap-4 md:grid-cols-3"
        @submit.prevent="registrationConfirmation = true"
      >
        <label class="label"
          >Đợt đăng ký<select
            v-model="registration.periodId"
            class="field"
            required
            @change="registration.date = selectedPeriod?.week || ''; viewDate = registration.date || viewDate"
          >
            <option :value="null">Chọn đợt</option>
            <option v-for="p in available" :key="p.id" :value="p.id">
              Tuần {{ dateLabel(p.week) }} · hạn
              {{ formatDateTime(p.deadline) }}
            </option>
          </select></label
        ><label class="label"
          >Ngày<select v-model="registration.date" class="field" required>
            <option v-for="day in registrationDays" :key="day" :value="day">
              {{ weekdayLabel(day) }}{{ data.closedDays?.[day] ? ' · nghỉ tiếp nhận' : '' }}
            </option>
          </select></label
        ><label class="label"
          >Ca<select
            v-model.number="registration.shiftId"
            class="field"
            required
          >
            <option v-for="s in data.shifts" :key="s.id" :value="s.id">
              {{ s.tenCa }} · {{ s.gioBatDau }}–{{ s.gioKetThuc }}
            </option>
          </select></label
        >
        <label class="label">Công việc trong ca<select v-model="registration.purpose" class="field">
          <option value="RECEPTION" :disabled="!!data.closedDays?.[registration.date]">Tiếp nhận / Spa</option>
          <option value="HOTEL_CARE">Chăm sóc Hotel đang lưu trú</option>
        </select></label>
        <button
          class="btn-primary justify-self-start"
          :disabled="busy || !available.length"
        >
          Đăng ký ca
        </button>
        <p v-if="!available.length" class="muted md:col-span-2">
          Chưa có đợt đăng ký đang mở. Admin cần mở đợt mới.
        </p>
      </form>
    </section>
    <section class="panel no-print">
      <h2>{{ admin ? "Phê duyệt / chuyển nhân viên" : "Đăng ký đã gửi" }}</h2>
      <div class="table-wrap mt-5">
        <table>
          <thead>
            <tr>
              <th>Nhân viên / Ngày</th>
              <th>Ca</th>
              <th>Trạng thái</th>
              <th v-if="admin">Xử lý</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in data.registrations" :key="r.id">
              <td>
                {{ employee(r.employeeId)?.tenNhanVien }}
                <p class="muted">{{ weekdayLabel(r.date) }}</p>
              </td>
              <td>{{ shift(r.shiftId)?.tenCa }}<p class="muted">{{ r.purpose === 'HOTEL_CARE' ? 'Chăm sóc Hotel' : 'Tiếp nhận / Spa' }}</p></td>
              <td>{{ r.status }}</td>
              <td v-if="admin">
                <div
                  v-if="
                    !data.periods.find((p) => p.id === r.periodId)?.published
                  "
                  class="flex flex-wrap gap-2"
                >
                  <label class="label"
                    ><span class="sr-only">Chuyển nhân viên</span
                    ><select v-model.number="reassign[r.id]" class="field">
                      <option
                        v-for="u in data.users.filter((u) => u.isActive)"
                        :key="u.id"
                        :value="u.id"
                      >
                        {{ u.tenNhanVien }}
                      </option>
                    </select></label
                  ><button
                    class="btn-primary"
                    :disabled="busy"
                    @click="
                      act('reviewShift', {
                        id: r.id,
                        decision: 'APPROVED',
                        employeeId: reassign[r.id] || r.employeeId,
                      })
                    "
                  >
                    Duyệt / chuyển</button
                  ><button
                    class="btn-danger"
                    :disabled="busy"
                    @click="
                      act('reviewShift', { id: r.id, decision: 'REJECTED' })
                    "
                  >
                    Từ chối
                  </button>
                </div>
                <span v-else>Đã chốt</span>
              </td>
            </tr>
            <tr v-if="!data.registrations.length">
              <td :colspan="admin ? 4 : 3" class="muted">Chưa có đăng ký.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <Dialog v-model:visible="registrationConfirmation" modal header="Xác nhận đăng ký ca" :style="{ width: '30rem', maxWidth: '95vw' }">
      <p>{{ weekdayLabel(registration.date) }} · {{ shift(registration.shiftId)?.tenCa }}</p>
      <p class="muted mt-2">{{ registration.purpose === 'HOTEL_CARE' ? 'Chăm sóc pet Hotel đang lưu trú' : 'Tiếp nhận / Spa' }}. Đăng ký sẽ chờ Admin duyệt.</p>
      <ActionFeedback :error="error" />
      <div class="mt-5 flex justify-end gap-2"><button class="btn" :disabled="busy" @click="registrationConfirmation = false">Quay lại</button><button class="btn-primary" :disabled="busy" @click="register">Gửi đăng ký</button></div>
    </Dialog>
  </div>
</template>
<script setup>
import { ref, reactive, computed, onMounted, watch } from "vue";
import { scheduleService } from "@/services/api/scheduleService";
import { useAction } from "@/composables/useAction";
import { localDate, addDays, weekDays, dateLabel } from "@/utils/dates";
import { formatDateTime } from "@/utils/formatters";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
import Dialog from 'primevue/dialog';
import { weekdayLabel } from '@/utils/dates';
defineProps({ admin: Boolean });
const data = ref({
    periods: [],
    registrations: [],
    assignments: [],
    users: [],
    shifts: [],
    closedDays: {},
  }),
  viewDate = ref(localDate()),
  nextWeek = addDays(weekDays()[0], 7),
  period = reactive({
    week: nextWeek,
    openAt: localDate() + "T08:00",
    deadline: addDays(localDate(), 5) + "T18:00",
    capacity: 3,
  }),
  registration = reactive({ periodId: null, date: "", shiftId: 1, purpose: 'RECEPTION' }),
  registrationConfirmation = ref(false),
  holiday = reactive({ week: nextWeek, dates: [], reason: 'Nghỉ tiếp nhận' }),
  reassign = reactive({}),
  { busy, error, message, perform } = useAction();
const days = computed(() => weekDays(viewDate.value || localDate())),
  available = computed(() =>
    data.value.periods.filter(
      (p) =>
        p.open &&
        !p.published &&
        Date.now() >= Date.parse(p.openAt) &&
        Date.now() < Date.parse(p.deadline),
    ),
  ),
  selectedPeriod = computed(() =>
    data.value.periods.find((p) => p.id === registration.periodId),
  ),
  registrationDays = computed(() =>
    selectedPeriod.value ? weekDays(selectedPeriod.value.week) : [],
  );
const holidayDays = computed(() => holiday.week ? weekDays(holiday.week) : []);
const holidayLocked = computed(() => data.value.periods.some((p) => p.week === holiday.week && (p.published || p.openedAt || (p.open && Date.now() >= Date.parse(p.openAt)))));
function loadHolidayDates() { holiday.dates = holidayDays.value.filter((day) => data.value.closedDays?.[day]); }
watch(() => holiday.week, loadHolidayDates);
function alreadyRegistered(day, shiftId) { return data.value.registrations.some((r) => r.date === day && r.shiftId === shiftId && r.status !== 'REJECTED'); }
function selectShift(day, shiftId) {
  const p = available.value.find((p) => weekDays(p.week).includes(day));
  if (!p) return;
  Object.assign(registration, { periodId: p.id, date: day, shiftId, purpose: data.value.closedDays?.[day] ? 'HOTEL_CARE' : 'RECEPTION' });
  registrationConfirmation.value = true;
}
function shift(id) {
  return data.value.shifts.find((s) => s.id === id);
}
function employee(id) {
  return data.value.users.find((u) => u.id === id);
}
async function get() {
  data.value = (await scheduleService.getSchedule()).data;
  loadHolidayDates();
  data.value.registrations.forEach((r) => (reassign[r.id] = r.employeeId));
}
async function load() {
  await perform(get);
}
onMounted(load);
async function act(action, p) {
  await perform(async () => {
    await scheduleService[action](p);
    await get();
  }, "Đã lưu lịch.");
}
function createPeriod() {
  return act("createPeriod", {
    ...period,
    openAt: period.openAt + ":00+07:00",
    deadline: period.deadline + ":00+07:00",
  });
}
function register() {
  return perform(async () => {
    await scheduleService.registerShift({ ...registration });
    registrationConfirmation.value = false;
    await get();
  }, 'Đã gửi đăng ký ca.');
}
function saveClosedDays() { return act('updateClosedDays', { week: holiday.week, dates: [...holiday.dates], reason: holiday.reason }); }
function print() {
  window.print();
}
</script>
