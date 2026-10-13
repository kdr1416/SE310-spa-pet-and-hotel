<template>
  <ol
    class="grid grid-cols-2 gap-4 md:grid-cols-3"
    aria-label="Tiến trình lịch hẹn"
  >
    <li
      v-for="(step, i) in steps"
      :key="step.status"
      class="flex items-center gap-3 text-sm"
      :aria-current="current === i ? 'step' : undefined"
    >
      <span
        :class="[
          'grid h-9 w-9 shrink-0 place-items-center rounded-full border font-bold',
          i <= current
            ? 'border-primary-700 bg-primary-700 text-white'
            : 'border-slate-300 bg-white text-slate-600',
        ]"
        ><Check v-if="i < current" :size="16" aria-hidden="true" /><span
          v-else
          >{{ i + 1 }}</span
        ></span
      ><span>{{ step.label }}</span>
    </li>
  </ol>
  <p v-if="status === 'CANCELLED'" class="error mt-5">
    Đơn đã bị từ chối / hủy.
  </p>
</template>
<script setup>
import { computed } from "vue";
import { Check } from "lucide-vue-next";
const p = defineProps({ status: String, checkedIn: Boolean });
const steps = [
  { status: "PENDING", label: "Chờ xác nhận" },
  { status: "CONFIRMED", label: "Đã xác nhận" },
  { status: "WAITING", label: "Chờ tiếp nhận" },
  { status: "IN_PROGRESS", label: "Đang chăm sóc" },
  { status: "READY", label: "Sẵn sàng bàn giao" },
  { status: "COMPLETED", label: "Hoàn tất" },
];
const current = computed(() =>
  p.status === "CONFIRMED" ? 2 : steps.findIndex((s) => s.status === p.status),
);
</script>
