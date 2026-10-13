<template>
  <section class="panel">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div>
        <h2 class="text-xl font-bold text-slate-900">Nhịp tiếp nhận</h2>
        <p class="muted mt-1 text-xs">Theo ngày nhận lịch, không tính đơn đã hủy.</p>
      </div>
      <span class="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-bold text-amber-900">
        <span class="h-1.5 w-1.5 rounded-full bg-accent-500"></span>
        7 ngày gần nhất
      </span>
    </div>
    <svg viewBox="0 0 600 160" class="mt-4 w-full" role="img" aria-label="Xu hướng số lịch nhận trong 7 ngày; số liệu chi tiết ở bên dưới">
      <defs>
        <linearGradient id="trendGradient" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="currentColor" class="text-primary" stop-opacity="0.18" />
          <stop offset="100%" stop-color="currentColor" class="text-primary" stop-opacity="0.0" />
        </linearGradient>
      </defs>
      <path d="M20 140H580 M20 80H580 M20 20H580" class="stroke-app-border" stroke-dasharray="4 4" fill="none" />
      <polygon :points="areaPoints" fill="url(#trendGradient)" />
      <polyline :points="points" fill="none" class="stroke-primary" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" />
      <circle v-for="(p, i) in data" :key="p.date" :cx="20 + i * (560 / 6)" :cy="140 - p.count / maximum * 120" r="5" class="fill-primary stroke-white" stroke-width="2.5" />
    </svg>
    <ol class="mt-4 grid grid-cols-7 gap-1.5 text-center text-xs" aria-label="Số lịch nhận theo ngày">
      <li v-for="p in data" :key="p.date" class="rounded-xl border border-app-border bg-app-subtle p-2 transition-colors hover:bg-primary-50">
        <span class="block text-[11px] font-semibold text-stone-500">{{ p.date.slice(8) }}/{{ p.date.slice(5, 7) }}</span>
        <strong class="mt-1 block font-heading text-sm font-bold" :class="p.count > 0 ? 'text-primary-600' : 'text-slate-700'">{{ p.count }}</strong>
      </li>
    </ol>
  </section>
</template>
<script setup>
import { computed } from "vue";
const props = defineProps({ data: { type: Array, required: true } });
const maximum = computed(() => Math.max(1, ...props.data.map(p => p.count)));
const points = computed(() => props.data.map((p, i) => `${20 + i * (560 / 6)},${140 - p.count / maximum.value * 120}`).join(" "));
const areaPoints = computed(() => `20,140 ${points.value} 580,140`);
</script>
