<template>
  <div class="space-y-6">
    <section class="overview-banner">
      <div class="relative z-10 flex flex-col justify-between gap-6 md:flex-row md:items-center">
        <div class="max-w-2xl">
          <div class="inline-flex items-center gap-1.5 rounded-full border border-white/30 bg-white/15 px-3.5 py-1 text-xs font-bold text-white backdrop-blur-xs shadow-2xs">
            <PawPrint :size="13" class="text-amber-200" aria-hidden="true" />
            <span>{{ auth.userRoleLabel }} · {{ dateLabel(date) }}</span>
          </div>
          <h2 class="mt-3.5 font-heading text-2xl font-black text-white md:text-3xl">
            Chào {{ auth.userName }}.
          </h2>
          <p class="mt-2 max-w-xl text-sm leading-relaxed text-orange-50/95 font-medium">
            {{ isSpa ? 'Những người bạn nhỏ đang chờ bạn chăm sóc. Xem việc được giao và ca làm trong ngày ở đây.' : 'Một góc nhìn rõ ràng cho ngày làm việc: lịch tiếp nhận, việc cần xử lý và những bước tiếp theo.' }}
          </p>
          <div class="mt-5 flex flex-wrap items-center gap-3">
            <RouterLink :to="primaryLink.to" class="inline-flex items-center gap-2 rounded-xl bg-white px-5 py-2.5 font-heading text-sm font-extrabold text-orange-700 shadow-md transition-all hover:bg-orange-50 hover:shadow-lg active:scale-98">
              {{ primaryLink.label }} <ArrowUpRight :size="17" aria-hidden="true" />
            </RouterLink>
          </div>
        </div>
        <div class="hidden shrink-0 items-center gap-3 rounded-2xl border border-white/30 bg-white/95 p-4 text-center shadow-lg backdrop-blur-sm md:flex md:flex-col md:min-w-[170px]">
          <span class="grid h-12 w-12 place-items-center rounded-2xl bg-orange-500 text-white shadow-xs">
            <PawPrint :size="24" />
          </span>
          <div>
            <p class="text-xs font-bold uppercase tracking-wider text-stone-500">Trạng thái ngày</p>
            <p class="mt-0.5 font-heading text-sm font-black text-slate-800">Sẵn sàng phục vụ</p>
          </div>
        </div>
      </div>
      <div class="overview-banner-mark" aria-hidden="true">
        <PawPrint :size="130" :stroke-width="1.2" />
      </div>
    </section>

    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h2 class="text-xl font-bold text-slate-900">{{ isSpa ? 'Công việc của bạn' : 'Nhịp làm việc trong ngày' }}</h2>
        <p class="muted mt-1">{{ isSpa ? 'Chỉ pet đã tiếp nhận và được phân công cho bạn.' : 'Lịch theo ngày nhận; việc chờ xử lý gồm toàn bộ đơn còn mở.' }}</p>
      </div>
      <div class="flex flex-wrap items-end gap-3">
        <label class="label text-xs font-bold text-stone-600">
          Ngày xem
          <input v-model="date" type="date" required class="field !py-2 !text-sm shadow-2xs" />
        </label>
        <button class="btn !min-h-10 px-3.5 text-xs shadow-2xs" :disabled="busy" @click="load">
          <RefreshCw :size="15" aria-hidden="true" :class="{ 'animate-spin': busy }" />
          {{ busy ? 'Đang tải' : 'Tải lại' }}
        </button>
      </div>
    </div>
    <p v-if="busy" role="status" class="muted">Đang tải dữ liệu tổng quan…</p>
    <div v-for="(message, source) in errors" :key="source" role="alert" class="error">{{ message }} <button class="ml-2 underline font-bold" :disabled="busy" @click="load">Thử lại</button></div>
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <MetricCard v-for="metric in metrics" :key="metric.label" v-bind="metric" />
    </div>

    <div class="grid items-start gap-6 xl:grid-cols-[1.35fr_1fr]">
      <div class="space-y-6">
        <section class="panel">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
              <p class="eyebrow flex items-center gap-1.5"><PawPrint :size="12" class="text-primary-600" />{{ isSpa ? 'Được phân công' : 'Theo ca tiếp nhận' }}</p>
              <h2 class="mt-1.5 text-xl font-bold text-slate-900">{{ isSpa ? 'Pet cần bạn chăm sóc' : 'Lịch nhận trong ngày' }}</h2>
            </div>
            <RouterLink :to="isSpa ? '/staff/care-tasks' : bookingPath" class="inline-flex items-center gap-1 text-sm font-bold text-primary-700 hover:text-primary-800">
              Xem tất cả <ArrowUpRight :size="15" />
            </RouterLink>
          </div>
          <template v-if="workspace">
            <ul v-if="isSpa && summary.tasks.length" class="mt-4 divide-y divide-app-border">
              <li v-for="task in summary.tasks.slice(0, 5)" :key="task.detail.id" class="flex flex-wrap items-center justify-between gap-3 py-3.5 rounded-xl px-2.5 transition-colors hover:bg-app-subtle">
                <div class="flex items-center gap-3">
                  <div class="grid h-10 w-10 shrink-0 place-items-center rounded-2xl bg-primary-500 text-white font-bold text-xs shadow-2xs">
                    <PawPrint :size="18" />
                  </div>
                  <div>
                    <p class="font-bold text-slate-900">{{ task.detail.thuCung.tenThuCung }}</p>
                    <p class="muted mt-0.5 text-xs">{{ task.detail.label }} · {{ task.booking.trackingCode }}</p>
                  </div>
                </div>
                <span class="badge rounded-full px-3 py-0.5 text-xs font-bold shadow-2xs bg-amber-50 text-amber-900 border border-amber-200">{{ getCareStatusBadge(task.detail.trangThaiChamSoc).label }}</span>
              </li>
            </ul>
            <ul v-else-if="!isSpa && summary.arrivals.length" class="mt-4 divide-y divide-app-border">
              <li v-for="booking in orderedArrivals.slice(0, 5)" :key="booking.id" class="flex flex-wrap items-center justify-between gap-3 py-3.5 rounded-xl px-2.5 transition-colors hover:bg-app-subtle">
                <div class="flex items-center gap-3">
                  <div class="grid h-10 w-10 shrink-0 place-items-center rounded-2xl bg-primary-50 text-primary-700 border border-primary-200 font-bold text-xs shadow-2xs">
                    Ca {{ booking.shiftId }}
                  </div>
                  <div>
                    <p class="font-bold text-slate-900">{{ booking.khachHang.tenKhachHang }}</p>
                    <p class="muted mt-0.5 text-xs">{{ booking.chiTietDonList.filter(d => !d.laDichVuThem).length }} bé · {{ booking.trackingCode }}</p>
                  </div>
                </div>
                <span class="badge rounded-full px-3 py-0.5 text-xs font-bold shadow-2xs bg-amber-50 text-amber-900 border border-amber-200">{{ getBookingStatusBadge(booking.trangThai).label }}</span>
              </li>
            </ul>
            <div v-else class="overview-empty mt-4">
              <div class="grid h-11 w-11 place-items-center rounded-2xl bg-orange-100 text-orange-600 shadow-2xs">
                <CalendarDays :size="22" aria-hidden="true" />
              </div>
              <p class="font-bold text-slate-800">{{ isSpa ? 'Chưa có pet được phân công trong các đơn còn mở.' : 'Chưa có lịch nhận trong ngày này.' }}</p>
              <span class="text-xs text-stone-500">{{ isSpa ? 'Pet sẽ xuất hiện sau khi quầy tiếp nhận và phân công.' : 'Chọn ngày khác hoặc mở danh sách lịch hẹn.' }}</span>
            </div>
          </template>
          <p v-else class="muted mt-5">{{ busy ? 'Đang tải lịch…' : 'Chưa tải được dữ liệu lịch.' }}</p>
        </section>

        <section v-if="!isSpa" class="overview-attention">
          <div class="flex items-center gap-3">
            <div class="grid h-10 w-10 place-items-center rounded-2xl bg-primary-500 text-white shadow-xs">
              <ClipboardCheck :size="20" aria-hidden="true" />
            </div>
            <div>
              <h2 class="text-xl font-bold text-amber-950">Việc cần chú ý</h2>
              <p class="text-xs text-amber-800/80 font-medium mt-0.5">Các đơn cần kiểm tra và xử lý tiếp theo</p>
            </div>
          </div>
          <template v-if="workspace">
            <div class="mt-4 grid gap-3 sm:grid-cols-2">
              <RouterLink :to="bookingPath" class="overview-queue-link group">
                <div>
                  <span class="block text-xs uppercase tracking-wider text-amber-800/80 font-bold">Hàng đợi</span>
                  <span class="text-sm font-bold text-stone-900 mt-0.5 block">Đơn chờ duyệt</span>
                </div>
                <strong>{{ summary.pending.length }}</strong>
              </RouterLink>
              <RouterLink :to="bookingPath" class="overview-queue-link group">
                <div>
                  <span class="block text-xs uppercase tracking-wider text-amber-800/80 font-bold">Hoàn tất chăm sóc</span>
                  <span class="text-sm font-bold text-stone-900 mt-0.5 block">Sẵn sàng bàn giao</span>
                </div>
                <strong>{{ summary.ready.length }}</strong>
              </RouterLink>
            </div>
            <p class="mt-3.5 flex items-center gap-2 text-xs text-stone-500">
              <span class="h-1.5 w-1.5 shrink-0 rounded-full bg-primary-500"></span>
              <span>Đơn READY vẫn cần kiểm tra hóa đơn và thanh toán trước khi hoàn tất.</span>
            </p>
          </template>
          <p v-else class="muted mt-4">Chưa có dữ liệu để tổng hợp.</p>
        </section>
      </div>

      <div class="space-y-6">
        <TrendChart v-if="auth.isAdmin && workspace" :data="summary.trend" />
        <section v-if="auth.isAdmin" class="panel">
          <div class="flex items-center justify-between gap-3">
            <h2 class="text-xl font-bold text-slate-900">Yêu cầu đang mở</h2>
            <span class="inline-flex items-center rounded-full bg-primary-50 border border-primary-200 px-3 py-1 text-xs font-bold text-primary-900">{{ finance ? requests.length : '—' }} yêu cầu</span>
          </div>
          <template v-if="finance">
            <ul v-if="requests.length" class="mt-4 divide-y divide-app-border">
              <li v-for="request in requests.slice(0, 4)" :key="request.id" class="py-3.5 rounded-xl px-2 transition-colors hover:bg-app-subtle">
                <RouterLink :to="request.type === 'ADDON' ? bookingPath : '/admin/finance'" class="block text-primary-700">
                  <p class="text-sm font-bold flex items-center justify-between text-slate-900 hover:text-primary-700">{{ requestLabels[request.type] || request.type }} <ArrowUpRight :size="15" class="text-primary-600" /></p>
                  <p class="muted mt-1 text-xs">{{ request.trackingCode }}</p>
                </RouterLink>
              </li>
            </ul>
            <p v-else class="muted mt-5">Không có yêu cầu đang chờ xử lý.</p>
          </template>
          <p v-else class="muted mt-5">{{ busy ? 'Đang tải yêu cầu…' : 'Chưa tải được dữ liệu tài chính.' }}</p>
        </section>
        <section v-else class="panel">
          <div class="flex items-center justify-between gap-3">
            <h2 class="text-xl font-bold text-slate-900">Ca làm trong ngày</h2>
            <div class="grid h-10 w-10 place-items-center rounded-2xl bg-primary-500 text-white shadow-xs">
              <Clock3 :size="18" aria-hidden="true" />
            </div>
          </div>
          <template v-if="schedule">
            <ul v-if="shifts.length" class="mt-4 space-y-3">
              <li v-for="shift in shifts" :key="shift.id" class="rounded-xl border border-amber-200 bg-gradient-to-br from-pastel-amber to-white p-4 shadow-2xs">
                <p class="font-bold text-slate-900">{{ shiftName(shift.shiftId)?.tenCa || `Ca ${shift.shiftId}` }}</p>
                <p class="mt-1 text-sm text-primary-800 font-semibold flex items-center gap-1.5">
                  <Clock3 :size="14" class="text-primary-600" />
                  {{ shiftName(shift.shiftId)?.gioBatDau }}–{{ shiftName(shift.shiftId)?.gioKetThuc }}
                </p>
              </li>
            </ul>
            <p v-else class="muted mt-5">Bạn chưa có ca đã công bố trong ngày này.</p>
          </template>
          <p v-else class="muted mt-5">{{ busy ? 'Đang tải ca làm…' : 'Chưa tải được lịch làm việc.' }}</p>
          <RouterLink to="/staff/dashboard" class="mt-4 inline-flex items-center gap-1 text-sm font-bold text-primary-700 hover:text-primary-800">
            Xem lịch và đăng ký ca <ArrowUpRight :size="15" />
          </RouterLink>
        </section>
        <section class="panel">
          <p class="eyebrow">Đi tiếp từ đây</p>
          <div class="mt-3.5 space-y-2.5">
            <RouterLink v-for="link in shortcuts" :key="link.to" :to="link.to" class="overview-shortcut group">
              <span>{{ link.label }}</span>
              <ArrowUpRight :size="16" class="text-stone-400 group-hover:text-accent-600 group-hover:translate-x-0.5 group-hover:-translate-y-0.5 transition-transform" aria-hidden="true" />
            </RouterLink>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>
<script setup>
import { computed, onMounted, ref } from "vue";
import { ArrowUpRight, PawPrint, CalendarDays, RefreshCw, ClipboardCheck, Clock3, HeartHandshake, Wallet, CheckCircle2 } from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { bookingService } from "@/services/api/bookingService";
import { financeService } from "@/services/api/financeService";
import { scheduleService } from "@/services/api/scheduleService";
import { dashboardSummary, publishedShifts } from "@/utils/dashboardSummary";
import { localDate, dateLabel } from "@/utils/dates";
import { formatCurrency, getBookingStatusBadge, getCareStatusBadge } from "@/utils/formatters";
import MetricCard from "./MetricCard.vue";
import TrendChart from "./TrendChart.vue";

const auth = useAuthStore();
const date = ref(localDate()), workspace = ref(null), finance = ref(null), schedule = ref(null), errors = ref({}), busy = ref(false);
const isSpa = computed(() => !auth.isAdmin && auth.can("CARE_UPDATE"));
const bookingPath = computed(() => auth.isAdmin ? "/admin/bookings" : "/staff/bookings");
const primaryLink = computed(() => isSpa.value ? { to: "/staff/care-tasks", label: "Mở công việc của bạn" } : { to: bookingPath.value, label: "Mở lịch booking" });
const summary = computed(() => dashboardSummary(workspace.value?.bookings || [], auth.user, date.value || localDate(), finance.value?.ledger || []));
const orderedArrivals = computed(() => [...summary.value.arrivals].sort((a, b) => a.shiftId - b.shiftId));
const shifts = computed(() => publishedShifts(schedule.value || {}, auth.user?.id, date.value || localDate()));
const requests = computed(() => (finance.value?.requests || []).filter(r => r.status === "PENDING" && ["ADDON", "COMPLAINT", "REFUND"].includes(r.type)));
const requestLabels = { ADDON: "Thêm dịch vụ", REFUND: "Hoàn tiền", COMPLAINT: "Khiếu nại", REVIEW: "Đánh giá" };
const shiftName = id => schedule.value?.shifts.find(s => s.id === id);
const metrics = computed(() => {
  const s = summary.value, known = workspace.value !== null, value = n => known ? n : "—";
  if (isSpa.value) return [
    { label: "Pet được phân công", value: value(s.tasks.length), hint: "Các đơn còn mở", icon: HeartHandshake, variant: "orange" },
    { label: "Chờ bạn bắt đầu", value: value(s.tasks.filter(t => t.detail.trangThaiChamSoc === "WAITING").length), hint: "Đã tiếp nhận", icon: Clock3, variant: "amber" },
    { label: "Đang chăm sóc", value: value(s.tasks.filter(t => ["IN_PROGRESS", "PAUSED"].includes(t.detail.trangThaiChamSoc)).length), hint: "Gồm pet đang tạm dừng", icon: PawPrint, variant: "emerald" },
    { label: "Sẵn sàng bàn giao", value: value(s.tasks.filter(t => t.detail.trangThaiChamSoc === "READY").length), hint: "Cần quầy hoàn tất bàn giao", icon: CheckCircle2, variant: "blue" },
  ];
  return [
    { label: "Lịch nhận trong ngày", value: value(s.arrivals.length), hint: dateLabel(s.today), icon: CalendarDays, variant: "orange" },
    { label: "Đơn chờ duyệt", value: value(s.pending.length), hint: "Toàn bộ đơn đang chờ", icon: ClipboardCheck, variant: "amber" },
    { label: auth.isAdmin ? "Pet đang chăm sóc" : "Pet chưa check-in", value: value(auth.isAdmin ? s.caring : s.awaitingCheckIn), hint: auth.isAdmin ? "Các đơn còn mở" : "Lịch xác nhận trong ngày", icon: HeartHandshake, variant: "emerald" },
    auth.isAdmin ? { label: "Thu ròng trong ngày", value: finance.value ? formatCurrency(s.net) : "—", hint: "Tiền đã thu trừ khoản hoàn", icon: Wallet, variant: "blue" } : { label: "Đơn chờ bàn giao", value: value(s.ready.length), hint: "Toàn bộ đơn READY", icon: CheckCircle2, variant: "blue" },
  ];
});
const shortcuts = computed(() => auth.isAdmin ? [{ to: "/admin/dashboard", label: "Thống kê tài chính" }, { to: "/admin/staff-schedule", label: "Lịch nhân viên" }] : [{ to: bookingPath.value, label: "Danh sách lịch hẹn" }, { to: "/staff/dashboard", label: "Lịch làm việc của bạn" }]);

async function load() {
  if (busy.value) return;
  busy.value = true;
  errors.value = {};
  workspace.value = finance.value = schedule.value = null;
  const source = async (key, label, fn, target) => {
    try { target.value = (await fn()).data; }
    catch (e) { errors.value[key] = `${label}: ${e.message || "Không tải được dữ liệu."}`; }
  };
  await Promise.all([
    source("workspace", "Lịch hẹn", () => bookingService.getWorkspace(), workspace),
    auth.isAdmin
      ? source("finance", "Tài chính", () => financeService.getFinance(), finance)
      : source("schedule", "Lịch làm việc", () => scheduleService.getSchedule(), schedule),
  ]);
  busy.value = false;
}
onMounted(load);
</script>
