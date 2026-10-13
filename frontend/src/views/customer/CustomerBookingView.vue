<template>
  <div class="page space-y-8">
    <div>
      <p class="eyebrow">Đặt lịch nhiều bé</p>
      <h1 class="mt-3">Một lịch hẹn, nhiều niềm vui.</h1>
      <p class="muted mt-4">Thêm từng bé, chọn dịch vụ và thời gian phù hợp.</p>
    </div>
    <section v-if="created" class="panel space-y-5" role="status">
      <h2>Đã lưu lịch hẹn</h2>
      <p class="break-all font-mono text-lg font-bold">
        {{ created.trackingCode }}
      </p>
      <p class="muted">
        Lưu lại mã và dùng số điện thoại đăng ký để tra cứu. Bản mock không gửi
        email.
      </p>
      <PaymentQrPanel
        :code="created.trackingCode"
        :amount="created.tongTienTamTinh"
      /><RouterLink
        :to="{ path: '/tracking', query: { code: created.trackingCode } }"
        class="btn-primary"
        >Tra cứu lịch hẹn</RouterLink
      ><button class="btn ml-3" @click="created = null">Đặt lịch khác</button>
    </section>
    <BookingForm v-else @created="created = $event" />
  </div>
</template>
<script setup>
import { ref } from "vue";
import BookingForm from "@/components/booking/BookingForm.vue";
import PaymentQrPanel from "@/components/booking/PaymentQrPanel.vue";
const created = ref(null);
</script>
