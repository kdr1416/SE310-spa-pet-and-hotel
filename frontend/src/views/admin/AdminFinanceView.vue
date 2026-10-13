<template>
  <div class="space-y-6">
    <nav class="flex flex-wrap gap-2" aria-label="Các mục tài chính">
      <button
        v-for="t in tabs"
        :key="t.id"
        class="btn"
        :aria-pressed="tab === t.id"
        @click="tab = t.id"
      >
        {{ t.label }}</button
      ><button class="btn ml-auto" :disabled="busy" @click="load">
        Tải lại
      </button>
    </nav>
    <ActionFeedback :error="error" :busy="busy" :message="message" />
    <section v-if="tab === 'prices'" class="panel space-y-6">
      <div v-for="group in priceGroups" :key="group.kind">
        <h2>{{ group.label }}</h2>
        <form
          v-for="entry in data.catalog[group.kind]"
          :key="entry.id"
          class="mt-4 flex flex-wrap items-end justify-between gap-4 border-b border-primary-100 pb-4"
          @submit.prevent="price(group.kind, entry)"
        >
          <p class="max-w-md text-sm font-bold">
            {{ entry.tenDichVu || entry.tenCombo || entry.tenPhong }}
          </p>
          <label class="label"
            >Giá (VND)<input
              v-model.number="prices[group.kind + entry.id]"
              class="field"
              type="number"
              min="0"
              step="1"
              required /></label
          ><button class="btn-primary" :disabled="busy">Lưu giá</button>
        </form>
      </div>
    </section>
    <section v-if="tab === 'invoices'" class="panel">
      <h2>Danh sách hóa đơn snapshot</h2>
      <div class="table-wrap mt-5">
        <table>
          <thead>
            <tr>
              <th>Đơn</th>
              <th>Thời điểm</th>
              <th>Số tiền</th>
              <th>Trạng thái</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="b in data.bookings.filter((b) => b.hoaDon)" :key="b.id">
              <td>
                <strong>{{ b.trackingCode }}</strong>
                <details class="mt-3">
                  <summary>Khoản mục</summary>
                  <p
                    v-for="line in b.hoaDon.ctHoaDonList"
                    :key="line.id"
                    class="muted"
                  >
                    {{ line.tenKhoanMuc }} ·
                    {{ formatCurrency(line.donGiaSnapshot) }}
                  </p>
                </details>
              </td>
              <td>{{ formatDateTime(b.hoaDon.createdAt) }}</td>
              <td>{{ formatCurrency(b.hoaDon.tongThanhToan) }}</td>
              <td>{{ b.hoaDon.trangThaiThanhToan }}</td>
            </tr>
            <tr v-if="!data.bookings.some((b) => b.hoaDon)">
              <td colspan="4" class="muted">Chưa có hóa đơn.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <section v-if="tab === 'requests'" class="panel">
      <h2>Khiếu nại & hoàn tiền</h2>
      <article
        v-for="r in data.requests.filter((r) =>
          ['COMPLAINT', 'REFUND'].includes(r.type),
        )"
        :key="r.id"
        class="mt-5 rounded-xl border border-primary-100 p-4"
      >
        <p class="font-bold">
          {{ r.trackingCode }} ·
          {{ r.type === "REFUND" ? "Hoàn tiền" : "Khiếu nại" }} · {{ r.status }}
        </p>
        <p class="mt-3 text-sm">{{ r.note }}</p>
        <form
          v-if="r.status === 'PENDING'"
          class="mt-4 grid gap-3 sm:grid-cols-2"
          @submit.prevent="review(r, 'APPROVED')"
        >
          <label v-if="r.type === 'REFUND'" class="label"
            >Số tiền hoàn<input
              v-model.number="refunds[r.id]"
              class="field"
              type="number"
              min="1"
              step="1"
              required /></label
          ><label class="label"
            >Nội dung xử lý<input
              v-model="resolutions[r.id]"
              class="field"
              maxlength="1000"
          /></label>
          <div class="flex gap-2">
            <button class="btn-primary" :disabled="busy">Phê duyệt</button
            ><button
              class="btn-danger"
              type="button"
              :disabled="busy"
              @click="review(r, 'REJECTED')"
            >
              Từ chối
            </button>
          </div>
        </form>
        <p v-else class="muted mt-3">
          {{ r.resolution }}
          {{ r.amount ? " · " + formatCurrency(r.amount) : "" }}
        </p>
      </article>
      <p
        v-if="
          !data.requests.some((r) => ['COMPLAINT', 'REFUND'].includes(r.type))
        "
        class="muted mt-5"
      >
        Chưa có yêu cầu.
      </p>
    </section>
    <section v-if="tab === 'reviews'" class="panel">
      <h2>Đánh giá khách hàng (chỉ Admin)</h2>
      <article
        v-for="r in data.requests.filter((r) => r.type === 'REVIEW')"
        :key="r.id"
        class="mt-4 border-b border-primary-100 pb-4"
      >
        <p class="font-bold">{{ r.trackingCode }} · {{ r.rating }} / 5 sao</p>
        <p class="muted">{{ r.note }}</p>
      </article>
      <p
        v-if="!data.requests.some((r) => r.type === 'REVIEW')"
        class="muted mt-5"
      >
        Chưa có đánh giá.
      </p>
    </section>
    <section v-if="tab === 'surcharges'" class="panel">
      <h2>Cài đặt phụ phí cố định</h2>
      <p class="muted mt-3">
        Quầy chọn phụ phí và nhập lý do trước khi tạo hóa đơn. Không thay đổi
        snapshot đã thanh toán.
      </p>
      <form
        class="mt-5 grid gap-4 sm:grid-cols-3"
        @submit.prevent="saveSurcharge"
      >
        <label class="label"
          >Tên phụ phí<input
            v-model="surcharge.name"
            class="field"
            required /></label
        ><label class="label"
          >Giá (VND)<input
            v-model.number="surcharge.amount"
            class="field"
            type="number"
            min="0"
            required /></label
        ><button class="btn-primary self-end" :disabled="busy">
          {{ surcharge.id ? "Cập nhật" : "Thêm phụ phí" }}
        </button>
      </form>
      <ul class="mt-5 space-y-3">
        <li
          v-for="r in data.surchargeRules"
          :key="r.id"
          class="flex items-center justify-between gap-3 border-b border-primary-100 pb-3"
        >
          <span>{{ r.name }} · {{ formatCurrency(r.amount) }}</span
          ><button class="btn" @click="Object.assign(surcharge, r)">Sửa</button>
        </li>
      </ul>
      <button
        v-if="surcharge.id"
        class="btn mt-3"
        @click="Object.assign(surcharge, { id: null, name: '', amount: 0 })"
      >
        Hủy sửa
      </button>
    </section>
  </div>
</template>
<script setup>
import { ref, reactive, onMounted } from "vue";
import { financeService } from "@/services/api/financeService";
import { bookingService } from "@/services/api/bookingService";
import { useAction } from "@/composables/useAction";
import { formatCurrency, formatDateTime } from "@/utils/formatters";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const data = ref({
    catalog: { services: [], combos: [], rooms: [] },
    bookings: [],
    requests: [],
    surchargeRules: [],
  }),
  tab = ref("prices"),
  prices = reactive({}),
  refunds = reactive({}),
  resolutions = reactive({}),
  surcharge = reactive({ id: null, name: "", amount: 0 }),
  { busy, error, message, perform } = useAction();
const tabs = [
    { id: "prices", label: "Giá dịch vụ / combo" },
    { id: "invoices", label: "Hóa đơn" },
    { id: "requests", label: "Khiếu nại / hoàn tiền" },
    { id: "reviews", label: "Đánh giá" },
    { id: "surcharges", label: "Phụ phí" },
  ],
  priceGroups = [
    { kind: "services", label: "Dịch vụ Spa" },
    { kind: "combos", label: "Combo" },
    { kind: "rooms", label: "Hotel / đêm" },
  ];
async function get() {
  data.value = (await financeService.getFinance()).data;
  for (const g of priceGroups)
    for (const e of data.value.catalog[g.kind])
      prices[g.kind + e.id] = e.giaCoBan ?? e.giaCombo ?? e.giaTheoNgay;
}
async function load() {
  await perform(get);
}
onMounted(load);
async function price(kind, e) {
  await perform(async () => {
    await financeService.updatePrice({
      kind,
      id: e.id,
      price: prices[kind + e.id],
    });
    await get();
  }, "Đã lưu giá danh mục.");
}
async function review(r, decision) {
  await perform(async () => {
    await bookingService.reviewRequest({
      id: r.id,
      decision,
      amount: refunds[r.id],
      resolution: resolutions[r.id] || "",
    });
    await get();
  }, "Đã xử lý yêu cầu.");
}
async function saveSurcharge() {
  await perform(async () => {
    await financeService.updateSurcharge({ ...surcharge });
    Object.assign(surcharge, { id: null, name: "", amount: 0 });
    await get();
  }, "Đã lưu phụ phí.");
}
</script>
