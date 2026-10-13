<template>
  <div class="space-y-6">
    <section class="panel flex flex-wrap items-end gap-4">
      <label class="label"
        >Thống kê theo<select v-model="granularity" class="field">
          <option value="day">Ngày</option>
          <option value="month">Tháng</option>
          <option value="year">Năm</option>
        </select></label
      ><label class="label"
        >Mốc ngày<input v-model="date" class="field" type="date" /></label
      ><button class="btn" :disabled="busy" @click="load">Tải lại</button>
    </section>
    <ActionFeedback :error="error" :busy="busy" />
    <div class="grid gap-5 md:grid-cols-3">
      <article class="panel">
        <p class="muted">Số đơn tạo trong kỳ</p>
        <p class="mt-4 text-3xl font-bold">{{ orders.length }}</p>
      </article>
      <article class="panel">
        <p class="muted">Khoản đã thu trong kỳ</p>
        <p class="mt-4 text-3xl font-bold">{{ formatCurrency(paid) }}</p>
      </article>
      <article class="panel">
        <p class="muted">Doanh thu sau hoàn tiền</p>
        <p class="mt-4 text-3xl font-bold">
          {{ formatCurrency(paid - refunded) }}
        </p>
      </article>
    </div>
    <p class="muted">
      Đơn tính theo ngày tạo tại Việt Nam. Doanh thu = tiền đã thu − khoản hoàn
      được duyệt trong kỳ; không cộng đơn chưa thanh toán.
    </p>
    <section class="panel">
      <h2>Giao dịch trong kỳ</h2>
      <div class="table-wrap mt-4">
        <table>
          <thead>
            <tr>
              <th>Ngày</th>
              <th>Loại</th>
              <th>Đơn</th>
              <th>Số tiền</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in transactions" :key="l.id">
              <td>{{ dateLabel(l.date) }}</td>
              <td>{{ l.type === "PAYMENT" ? "Thu tiền" : "Hoàn tiền" }}</td>
              <td>
                {{
                  data.bookings.find((b) => b.id === l.bookingId)?.trackingCode
                }}
              </td>
              <td>{{ formatCurrency(l.amount) }}</td>
            </tr>
            <tr v-if="!transactions.length">
              <td colspan="4" class="muted">Chưa có giao dịch trong kỳ.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
<script setup>
import { ref, computed, onMounted } from "vue";
import { financeService } from "@/services/api/financeService";
import { useAction } from "@/composables/useAction";
import { localDate, dateLabel } from "@/utils/dates";
import { formatCurrency } from "@/utils/formatters";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const data = ref({ bookings: [], ledger: [] }),
  date = ref(localDate()),
  granularity = ref("day"),
  { busy, error, perform } = useAction();
const prefix = computed(() =>
    date.value.slice(
      0,
      granularity.value === "year" ? 4 : granularity.value === "month" ? 7 : 10,
    ),
  ),
  orders = computed(() =>
    data.value.bookings.filter((b) =>
      localDate(new Date(b.createdAt)).startsWith(prefix.value),
    ),
  ),
  transactions = computed(() =>
    data.value.ledger.filter((l) => l.date.startsWith(prefix.value)),
  ),
  paid = computed(() =>
    transactions.value
      .filter((l) => l.type === "PAYMENT")
      .reduce((n, l) => n + l.amount, 0),
  ),
  refunded = computed(() =>
    transactions.value
      .filter((l) => l.type === "REFUND")
      .reduce((n, l) => n + l.amount, 0),
  );
async function load() {
  await perform(
    async () => (data.value = (await financeService.getFinance()).data),
  );
}
onMounted(load);
</script>
