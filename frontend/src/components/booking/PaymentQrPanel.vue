<template>
  <div class="rounded-xl border border-primary-200 bg-primary-50 p-4">
    <h3>QR thông tin tạm tính</h3>
    <div class="mt-4 flex flex-wrap items-center gap-4">
      <img
        v-if="image"
        :src="image"
        alt="QR thông tin lịch hẹn demo, không thể thanh toán"
        width="140"
        height="140"
      />
      <div>
        <p class="font-bold">{{ formatCurrency(amount) }}</p>
        <p class="muted mt-2 max-w-xs">
          Mã demo chứa mã lịch hẹn và phí tạm tính. Không phải VietQR và không
          chuyển tiền.
        </p>
      </div>
    </div>
    <p v-if="error" role="alert" class="error mt-3">{{ error }}</p>
  </div>
</template>
<script setup>
import { ref, watch } from "vue";
import QRCode from "qrcode";
import { formatCurrency } from "@/utils/formatters";
const props = defineProps({ code: String, amount: Number }),
  image = ref(""),
  error = ref("");
watch(
  () => [props.code, props.amount],
  async () => {
    error.value = "";
    try {
      image.value = await QRCode.toDataURL(
        JSON.stringify({
          type: "PETSPA_DEMO",
          code: props.code,
          estimate: props.amount,
        }),
        { width: 140, margin: 2, errorCorrectionLevel: "M" },
      );
    } catch {
      image.value = "";
      error.value = "Không tạo được QR demo.";
    }
  },
  { immediate: true },
);
</script>
