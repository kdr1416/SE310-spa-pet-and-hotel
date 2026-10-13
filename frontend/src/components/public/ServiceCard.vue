<template>
  <article
    class="flex flex-col overflow-hidden rounded-2xl border border-primary-100 bg-white shadow-card"
  >
    <div v-if="image" class="h-48">
      <StoredPhoto :blob="image" :alt="item.title" />
    </div>
    <div class="flex flex-1 flex-col gap-4 p-5">
      <span class="eyebrow">{{ item.category }}</span>
      <h3>{{ item.title }}</h3>
      <p class="muted flex-1">{{ item.description }}</p>
      <div class="flex items-center justify-between gap-3">
        <span class="font-bold"
          >{{ formatCurrency(item.price)
          }}<small v-if="item.category === 'Hotel'" class="muted">
            / đêm</small
          ></span
        ><button class="btn" @click="$emit('detail', item)">
          Chi tiết <ArrowRight :size="14" aria-hidden="true" />
        </button>
      </div>
    </div>
  </article>
</template>
<script setup>
import { ArrowRight } from "lucide-vue-next";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
import { formatCurrency } from "@/utils/formatters";
defineProps({ item: Object, image: String });
defineEmits(["detail"]);
</script>
