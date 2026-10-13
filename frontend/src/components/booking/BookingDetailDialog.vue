<template>
  <div class="space-y-5">
    <div class="flex flex-wrap justify-between gap-3">
      <div>
        <p class="break-all font-mono font-bold">{{ booking.trackingCode }}</p>
        <p class="muted">
          {{ booking.khachHang.tenKhachHang }} ·
          {{ booking.khachHang.soDienThoai }} · {{ dateLabel(booking.date) }}
        </p>
      </div>
      <span class="badge">{{
        getBookingStatusBadge(booking.trangThai).label
      }}</span>
    </div>
    <ActionFeedback :busy="busy" :error="error" :message="message" />
    <article
      v-for="d in booking.chiTietDonList"
      :key="d.id"
      class="rounded-xl border border-primary-100 p-4"
    >
      <div class="flex flex-wrap justify-between gap-3">
        <div>
          <h3>
            {{ d.thuCung.tenThuCung }}
            <small v-if="d.laDichVuThem">(dịch vụ thêm)</small>
          </h3>
          <p class="muted">
            {{ d.label }} · {{ getCareStatusBadge(d.trangThaiChamSoc).label }}
          </p>
        </div>
        <strong>{{ formatCurrency(d.donGiaChot) }}</strong>
      </div>
      <p v-if="d.thuCung.ghiChuDiUng" class="mt-3 text-sm text-red-800">
        Lưu ý: {{ d.thuCung.ghiChuDiUng }}
      </p>
      <details v-if="d.input.vaccine" class="mt-3">
        <summary class="font-bold">Giấy tiêm chủng</summary>
        <div class="mt-3 h-48">
          <StoredPhoto
            :blob="d.input.vaccine"
            alt="Giấy tiêm chủng, cần nhân viên kiểm tra"
          />
        </div>
      </details>
      <form
        v-if="canEdit && !d.checkedIn && booking.trangThai !== 'PENDING'"
        class="mt-4 grid gap-3 sm:grid-cols-3"
        @submit.prevent="checkIn(d)"
      >
        <label class="label"
          >Cân nặng thực tế<input
            v-model.number="checks[d.id].weight"
            type="number"
            min=".1"
            max="150"
            step=".1"
            required
            class="field" /></label
        ><label class="label"
          >Tình trạng lông<input
            v-model="checks[d.id].coat"
            class="field"
            required /></label
        ><label class="label"
          >Nhân viên spa<select
            v-model.number="checks[d.id].employeeId"
            class="field"
            required
          >
            <option :value="null">Chọn nhân viên</option>
            <option v-for="u in spaUsers" :key="u.id" :value="u.id">
              {{ u.tenNhanVien }}
            </option>
          </select></label
        ><button
          class="btn-primary justify-self-start sm:col-span-3"
          :disabled="busy"
        >
          Xác nhận tiếp nhận & chốt giá
        </button>
      </form>
      <p v-if="d.checkedIn" class="muted mt-3">
        Đã check-in {{ d.canNangThucTe }}kg · {{ d.tinhTrangLong }} ·
        {{
          store.users.find((u) => u.id === d.nhanVienId)?.tenNhanVien || "Spa"
        }}
      </p>
    </article>
    <template v-if="canEdit"
      ><form class="grid gap-3" @submit.prevent="saveNote">
        <label class="label"
          >Ghi chú đơn<textarea v-model="note" class="field" rows="2" /></label
        ><button class="btn justify-self-start" :disabled="busy">
          Lưu ghi chú
        </button>
      </form>
      <form
        v-if="store.catalog.surchargeRules.length"
        class="grid gap-3 sm:grid-cols-2"
        @submit.prevent="surcharge"
      >
        <label class="label"
          >Phụ phí<select v-model="surchargeId" class="field">
            <option
              v-for="r in store.catalog.surchargeRules"
              :key="r.id"
              :value="r.id"
            >
              {{ r.name }} · {{ formatCurrency(r.amount) }}
            </option>
          </select></label
        ><label class="label"
          >Lý do<input v-model="surchargeNote" class="field" required /></label
        ><button class="btn justify-self-start" :disabled="busy">
          Thêm phụ phí
        </button>
      </form>
      <button
        v-if="booking.trangThai === 'READY'"
        class="btn-primary"
        :disabled="busy"
        @click="invoice"
      >
        Tạo hóa đơn snapshot
      </button></template
    >
    <section
      v-if="booking.hoaDon"
      class="rounded-xl border border-primary-200 p-4"
    >
      <h3>Hóa đơn snapshot</h3>
      <ul class="mt-3 space-y-2 text-sm">
        <li
          v-for="line in booking.hoaDon.ctHoaDonList"
          :key="line.id"
          class="flex flex-wrap justify-between gap-2"
        >
          <span>{{ line.tenKhoanMuc }}</span
          ><strong>{{ formatCurrency(line.thanhTien) }}</strong>
        </li>
        <li v-for="f in booking.hoaDon.phuPhiList" :key="f.id">
          {{ f.name }} · {{ formatCurrency(f.amount) }} · {{ f.note }}
        </li>
      </ul>
      <p class="my-4 text-lg font-bold">
        Tổng: {{ formatCurrency(booking.hoaDon.tongThanhToan) }} ·
        {{ booking.hoaDon.trangThaiThanhToan }}
      </p>
      <template
        v-if="
          auth.can('BOOKING_MANAGE') &&
          booking.hoaDon.trangThaiThanhToan === 'UNPAID'
        "
        ><div class="flex flex-wrap gap-3">
          <button class="btn" @click="method = 'CASH'">Thu tiền mặt</button
          ><button class="btn" @click="method = 'BANK_TRANSFER'">
            Chuyển khoản (demo)
          </button>
        </div>
        <PaymentQrPanel
          v-if="method === 'BANK_TRANSFER'"
          class="mt-4"
          :code="booking.trackingCode"
          :amount="booking.hoaDon.tongThanhToan"
        />
        <div v-if="method" class="mt-4 rounded-xl bg-accent-50 p-4">
          <p class="muted">
            Xác nhận đã thu đủ tiền trong bản mock? Mở QR không ghi nhận thanh
            toán.
          </p>
          <button class="btn-primary mt-3" :disabled="busy" @click="pay">
            Xác nhận đã thu đủ (mock)
          </button>
        </div></template
      >
    </section>
  </div>
</template>
<script setup>
import { ref, reactive, computed, watch } from "vue";
import { useAuthStore } from "@/stores/auth";
import { useWorkspaceStore } from "@/stores/workspace";
import { bookingService } from "@/services/api/bookingService";
import { useAction } from "@/composables/useAction";
import { dateLabel } from "@/utils/dates";
import {
  formatCurrency,
  getBookingStatusBadge,
  getCareStatusBadge,
} from "@/utils/formatters";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
import PaymentQrPanel from "./PaymentQrPanel.vue";
const props = defineProps({ booking: Object }),
  emit = defineEmits(["changed"]),
  auth = useAuthStore(),
  store = useWorkspaceStore(),
  checks = reactive({}),
  note = ref(""),
  method = ref(""),
  surchargeId = ref(store.catalog.surchargeRules[0]?.id),
  surchargeNote = ref(""),
  { busy, error, message, perform } = useAction();
const canEdit = computed(
    () =>
      auth.can("BOOKING_MANAGE") &&
      !props.booking.hoaDon &&
      !["COMPLETED", "CANCELLED"].includes(props.booking.trangThai),
  ),
  spaUsers = computed(() =>
    store.users.filter(
      (u) => u.vaiTro === "STAFF" && u.workGroup === "SPA" && u.isActive,
    ),
  );
watch(
  () => props.booking,
  (b) => {
    note.value = b.ghiChuKhach || "";
    for (const d of b.chiTietDonList)
      checks[d.id] ||= {
        weight: d.thuCung.canNangDangKy || 5,
        coat: "Bình thường",
        employeeId: spaUsers.value[0]?.id || null,
      };
  },
  { immediate: true },
);
async function act(action, p) {
  await perform(async () => {
    await bookingService[action]({ id: props.booking.id, ...p });
    emit("changed");
  }, "Đã lưu dữ liệu.");
}
function checkIn(d) {
  return act("checkIn", { detailId: d.id, ...checks[d.id] });
}
function saveNote() {
  return act("updateBooking", { note: note.value });
}
function invoice() {
  return act("createInvoice");
}
function pay() {
  return act("confirmPayment", { method: method.value });
}
function surcharge() {
  return act("addSurcharge", {
    ruleId: surchargeId.value,
    note: surchargeNote.value,
  });
}
</script>
