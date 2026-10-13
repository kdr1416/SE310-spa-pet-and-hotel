<template>
  <div class="page space-y-8">
    <div>
      <p class="eyebrow">Theo dõi & lịch sử</p>
      <h1 class="mt-3">Lịch hẹn của bạn</h1>
      <p class="muted mt-4">
        Dùng mã lịch hẹn và số điện thoại đã đăng ký để tra cứu.
      </p>
    </div>
    <section class="panel">
      <form
        data-testid="lookup-form"
        class="grid items-end gap-4 md:grid-cols-[1fr_1fr_auto]"
        @submit.prevent="search"
      >
        <label class="label" for="tracking-code"
          >Mã tra cứu<input
            id="tracking-code"
            v-model="code"
            class="field uppercase"
            required
            autocomplete="off" /></label
        ><label class="label" for="tracking-phone"
          >Số điện thoại<input
            id="tracking-phone"
            v-model="phone"
            class="field"
            required
            type="tel" /></label
        ><button class="btn-accent" :disabled="store.loading">Tra cứu</button>
      </form>
      <ActionFeedback
        class="mt-4"
        :error="store.error || error"
        :busy="store.loading"
        :message="message"
      />
    </section>
    <div
      v-if="booking"
      data-testid="tracking-result"
      class="grid items-start gap-6 lg:grid-cols-[1fr_340px]"
    >
      <div class="space-y-6">
        <section class="panel space-y-6">
          <div class="flex flex-wrap justify-between gap-3">
            <h2>Tiến trình lịch hẹn</h2>
            <span class="badge">{{
              getBookingStatusBadge(booking.trangThai).label
            }}</span>
          </div>
          <BookingProgress :status="booking.trangThai" />
        </section>
        <section class="panel space-y-5">
          <h2>Tình trạng từng bé</h2>
          <article
            v-for="d in booking.chiTietDonList"
            :key="d.id"
            class="rounded-xl border border-primary-100 p-4"
          >
            <div class="flex flex-wrap justify-between gap-3">
              <div class="flex items-center gap-3">
                <span
                  class="grid h-10 w-10 place-items-center rounded-xl bg-primary-50"
                  ><Dog
                    v-if="d.thuCung.loaiPet === 'CHO'"
                    aria-hidden="true" /><Cat v-else aria-hidden="true"
                /></span>
                <div>
                  <h3>{{ d.thuCung.tenThuCung }}</h3>
                  <p class="muted">
                    {{ d.thuCung.giongLoai }} ·
                    {{ d.canNangThucTe || d.thuCung.canNangDangKy }}kg
                  </p>
                </div>
              </div>
              <span class="badge">{{
                getCareStatusBadge(d.trangThaiChamSoc).label
              }}</span>
            </div>
            <p class="muted mt-4">{{ d.label }}</p>
            <details class="mt-4">
              <summary class="font-bold text-primary-700">
                Nhật ký & lịch sử chăm sóc ({{ d.nhatKyChamSoc.length }})
              </summary>
              <div class="mt-4 grid gap-4 sm:grid-cols-2">
                <article v-for="log in d.nhatKyChamSoc" :key="log.id">
                  <div v-if="log.photo" class="h-40">
                    <StoredPhoto
                      :blob="log.photo"
                      :alt="'Nhật ký ' + d.thuCung.tenThuCung"
                    />
                  </div>
                  <p class="mt-2 text-sm">{{ log.ghiChuSucKhoe }}</p>
                  <p class="muted">{{ formatDateTime(log.thoiGianGhiNhan) }}</p>
                </article>
                <p v-if="!d.nhatKyChamSoc.length" class="muted">
                  Chưa có nhật ký chăm sóc.
                </p>
              </div>
            </details>
          </article>
          <div
            class="flex flex-wrap items-center justify-between gap-3 border-t border-primary-100 pt-4"
          >
            <p class="muted">
              Cập nhật: {{ formatDateTime(booking.updatedAt) }}
            </p>
            <button class="btn" :disabled="store.loading" @click="refresh">
              Tải cập nhật
            </button>
          </div>
        </section>
        <section
          v-if="canAddon || booking.trangThai === 'COMPLETED'"
          class="panel space-y-4"
        >
          <h2>
            {{
              booking.trangThai === "COMPLETED"
                ? "Phản hồi sau dịch vụ"
                : "Yêu cầu dịch vụ thêm"
            }}
          </h2>
          <form class="grid gap-4" @submit.prevent="sendRequest">
            <label class="label"
              >Loại yêu cầu<select v-model="request.type" class="field">
                <option v-if="canAddon" value="ADDON">Thêm dịch vụ</option>
                <template v-if="booking.trangThai === 'COMPLETED'"
                  ><option
                    value="REVIEW"
                    :disabled="
                      booking.requests?.some((r) => r.type === 'REVIEW')
                    "
                  >
                    Đánh giá (mỗi đơn một lần)
                  </option>
                  <option value="COMPLAINT">Khiếu nại</option>
                  <option value="REFUND">Yêu cầu hoàn tiền</option></template
                >
              </select></label
            ><template v-if="request.type === 'ADDON'"
              ><label class="label"
                >Thú cưng<select v-model="request.detailId" class="field">
                  <option
                    v-for="d in booking.chiTietDonList"
                    :key="d.id"
                    :value="d.id"
                  >
                    {{ d.thuCung.tenThuCung }}
                  </option>
                </select></label
              ><label class="label"
                >Dịch vụ<select
                  v-model.number="request.serviceId"
                  class="field"
                >
                  <option
                    v-for="s in catalog.services.filter(
                      (s) => s.isActive && s.loaiDichVu !== 'SPA_ADVANCED',
                    )"
                    :key="s.id"
                    :value="s.id"
                  >
                    {{ s.tenDichVu }}
                  </option>
                </select></label
              ></template
            ><label v-if="request.type === 'REVIEW'" class="label"
              >Số sao<select v-model.number="request.rating" class="field">
                <option v-for="n in 5" :key="n" :value="n">{{ n }} sao</option>
              </select></label
            ><label class="label"
              >Nội dung<textarea
                v-model="request.note"
                class="field"
                rows="3"
                required
                maxlength="2000"
              /></label
            ><button class="btn-primary justify-self-start" :disabled="busy">
              Gửi yêu cầu
            </button>
          </form>
        </section>
        <section v-if="booking.requests?.length" class="panel">
          <h2>Yêu cầu đã gửi</h2>
          <ul class="mt-4 space-y-4">
            <li
              v-for="r in booking.requests"
              :key="r.id"
              class="border-b border-primary-100 pb-3 text-sm"
            >
              <p class="font-bold">
                {{ requestLabels[r.type] }} · {{ r.status }}
              </p>
              <p>{{ r.note }}</p>
              <p v-if="r.resolution" class="muted">
                Phản hồi: {{ r.resolution }}
              </p>
            </li>
          </ul>
        </section>
      </div>
      <aside class="panel space-y-5">
        <h2>Thông tin lịch hẹn</h2>
        <p class="break-all font-mono font-bold">{{ booking.trackingCode }}</p>
        <dl class="space-y-3 text-sm">
          <div>
            <dt class="muted">Khách hàng</dt>
            <dd>{{ booking.khachHang.tenKhachHang }}</dd>
          </div>
          <div>
            <dt class="muted">Điện thoại</dt>
            <dd>
              {{ booking.khachHang.soDienThoai.slice(0, 3) }} •••
              {{ booking.khachHang.soDienThoai.slice(-3) }}
            </dd>
          </div>
          <div>
            <dt class="muted">Ngày tiếp nhận</dt>
            <dd>{{ dateLabel(booking.date) }}</dd>
          </div>
        </dl>
        <p class="font-bold">
          {{ booking.hoaDon ? "Hóa đơn" : "Phí tạm tính" }}:
          {{
            formatCurrency(
              booking.hoaDon?.tongThanhToan ?? booking.tongTienTamTinh,
            )
          }}
        </p>
        <template v-if="booking.hoaDon"
          ><p class="badge">{{ booking.hoaDon.trangThaiThanhToan }}</p>
          <ul class="space-y-3 text-sm">
            <li v-for="line in booking.hoaDon.ctHoaDonList" :key="line.id">
              {{ line.tenKhoanMuc }} · {{ formatCurrency(line.thanhTien) }}
            </li>
          </ul></template
        >
        <p class="muted">
          Lịch sử hiển thị cho mã đã xác minh; đánh giá chỉ được Admin xem.
        </p>
      </aside>
    </div>
  </div>
</template>
<script setup>
import { ref, computed, reactive, onBeforeUnmount } from "vue";
import { useRoute } from "vue-router";
import { Dog, Cat } from "lucide-vue-next";
import { useBookingStore } from "@/stores/booking";
import { bookingService } from "@/services/api/bookingService";
import { catalogService } from "@/services/api/catalogService";
import { useAction } from "@/composables/useAction";
import { dateLabel } from "@/utils/dates";
import {
  formatCurrency,
  formatDateTime,
  getBookingStatusBadge,
  getCareStatusBadge,
} from "@/utils/formatters";
import BookingProgress from "@/components/tracking/BookingProgress.vue";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const route = useRoute(),
  store = useBookingStore(),
  code = ref(String(route.query.code || "")),
  phone = ref(""),
  verified = ref(null),
  catalog = ref({ services: [] }),
  { busy, error, message, perform } = useAction();
const booking = computed(() => store.currentBooking),
  canAddon = computed(
    () =>
      booking.value &&
      ["CONFIRMED", "IN_PROGRESS"].includes(booking.value.trangThai) &&
      !booking.value.hoaDon,
  );
const request = reactive({
    type: "ADDON",
    detailId: "",
    serviceId: 1,
    note: "",
    rating: 5,
  }),
  requestLabels = {
    ADDON: "Thêm dịch vụ",
    REVIEW: "Đánh giá",
    COMPLAINT: "Khiếu nại",
    REFUND: "Hoàn tiền",
  };
async function search() {
  verified.value = null;
  const result = await store.fetchByTracking(code.value, phone.value);
  if (result.success) {
    verified.value = { trackingCode: code.value, phone: phone.value };
    request.type =
      result.data.trangThai === "COMPLETED" ? "COMPLAINT" : "ADDON";
    request.detailId = result.data.chiTietDonList[0]?.id || "";
    await perform(async () => {
      catalog.value = (await catalogService.catalog()).data;
    });
  }
}
async function refresh() {
  if (verified.value && !store.loading)
    await store.fetchByTracking(
      verified.value.trackingCode,
      verified.value.phone,
    );
}
async function sendRequest() {
  await perform(async () => {
    await bookingService.customerRequest({ ...verified.value, ...request });
    request.note = "";
    await refresh();
  }, "Đã gửi yêu cầu.");
}
const timer = setInterval(() => {
  if (!document.hidden && verified.value && !busy.value) refresh();
}, 10000);
onBeforeUnmount(() => {
  clearInterval(timer);
  store.currentBooking = null;
});
</script>
