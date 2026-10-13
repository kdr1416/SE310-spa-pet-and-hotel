<template>
  <article class="overview-metric group border" :class="theme.card">
    <div class="flex items-center justify-between gap-3">
      <p class="text-xs font-bold uppercase tracking-wider" :class="theme.label">
        {{ label }}
      </p>
      <div
        class="grid h-11 w-11 shrink-0 place-items-center rounded-2xl shadow-xs transition-transform group-hover:scale-105"
        :class="theme.badge"
      >
        <component :is="icon" :size="20" aria-hidden="true" />
      </div>
    </div>
    <p class="mt-3.5 font-heading text-3xl font-black tracking-tight md:text-4xl" :class="theme.value">
      {{ value }}
    </p>
    <div class="mt-2.5 flex items-center gap-2 text-xs font-semibold" :class="theme.hint">
      <span class="h-1.5 w-1.5 shrink-0 rounded-full" :class="theme.dot" aria-hidden="true"></span>
      <span>{{ hint }}</span>
    </div>
  </article>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  label: String,
  value: [String, Number],
  hint: String,
  icon: [Object, Function],
  variant: { type: String, default: "orange" },
  warm: Boolean,
});

const themes = {
  orange: {
    card: "bg-pastel-orange border-primary-200 shadow-card hover:border-primary-300 hover:shadow-warm-md",
    badge: "bg-primary-500 text-white",
    label: "text-primary-900/80",
    value: "text-primary-950",
    hint: "text-primary-800/85",
    dot: "bg-primary-500",
  },
  amber: {
    card: "bg-pastel-amber border-amber-200 shadow-card hover:border-amber-300 hover:shadow-warm-md",
    badge: "bg-amber-500 text-white",
    label: "text-amber-900/80",
    value: "text-amber-950",
    hint: "text-amber-800/85",
    dot: "bg-amber-500",
  },
  emerald: {
    card: "bg-pastel-emerald border-emerald-200 shadow-card hover:border-emerald-300 hover:shadow-warm-md",
    badge: "bg-emerald-500 text-white",
    label: "text-emerald-900/80",
    value: "text-emerald-950",
    hint: "text-emerald-800/85",
    dot: "bg-emerald-500",
  },
  blue: {
    card: "bg-pastel-blue border-blue-200 shadow-card hover:border-blue-300 hover:shadow-warm-md",
    badge: "bg-blue-500 text-white",
    label: "text-blue-900/80",
    value: "text-blue-950",
    hint: "text-blue-800/85",
    dot: "bg-blue-500",
  },
};

const theme = computed(() => themes[props.variant] || themes[props.warm ? "orange" : "orange"] || themes.orange);
</script>

