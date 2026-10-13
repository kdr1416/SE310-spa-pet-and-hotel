<template>
  <div class="page space-y-8">
    <div>
      <p class="eyebrow">Danh mục dịch vụ</p>
      <h1 class="mt-3">Chăm sóc theo nhu cầu của bé</h1>
      <p class="muted mt-4">
        Giá Spa theo cân nặng. Giá Hotel theo số đêm. Nhân viên xác nhận giá
        cuối khi tiếp nhận.
      </p>
    </div>
    <ActionFeedback :busy="busy" :error="error" />
    <div v-for="group in groups" :key="group.label">
      <h2 class="mb-5">{{ group.label }}</h2>
      <div class="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
        <ServiceCard
          v-for="item in group.items"
          :key="item.title"
          :item="item"
          @detail="selected = $event"
        />
      </div>
    </div>
    <Dialog
      v-model:visible="visible"
      modal
      header="Chi tiết dịch vụ"
      :style="{ width: '32rem', maxWidth: '95vw' }"
      ><div v-if="selected" class="space-y-5">
        <h3>{{ selected.title }}</h3>
        <p class="muted">{{ selected.description }}</p>
        <p class="font-bold">{{ formatCurrency(selected.price) }}</p>
        <p v-if="selected.advanced" class="muted">
          Dịch vụ cắt tỉa được đặt trong combo có tắm theo BR-05.
        </p>
        <RouterLink to="/booking" class="btn-accent">Đặt lịch</RouterLink>
      </div></Dialog
    >
  </div>
</template>
<script setup>
import { ref, computed, onMounted } from "vue";
import Dialog from "primevue/dialog";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAction } from "@/composables/useAction";
import ServiceCard from "@/components/public/ServiceCard.vue";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
import { formatCurrency } from "@/utils/formatters";
const store = useWorkspaceStore(),
  selected = ref(null),
  visible = computed({
    get: () => !!selected.value,
    set: (v) => {
      if (!v) selected.value = null;
    },
  }),
  { busy, error, perform } = useAction();
onMounted(() => perform(() => store.loadCatalog()));
const groups = computed(() => [
  {
    label: "Spa",
    items: store.catalog.services
      .filter((x) => x.isActive)
      .map((x) => ({
        title: x.tenDichVu,
        description: x.moTa,
        price: x.giaCoBan,
        category: "Spa",
        advanced: x.loaiDichVu === "SPA_ADVANCED",
      })),
  },
  {
    label: "Gói combo",
    items: store.catalog.combos
      .filter((x) => x.isActive)
      .map((x) => ({
        title: x.tenCombo,
        description: x.moTa,
        price: x.giaCombo,
        category: "Combo",
      })),
  },
  {
    label: "Khách sạn",
    items: store.catalog.rooms
      .filter((x) => x.isActive)
      .map((x) => ({
        title: x.tenPhong,
        description:
          "Lưu trú liên tục; cần ảnh giấy tiêm chủng. Giấy tờ được nhân viên kiểm tra khi tiếp nhận.",
        price: x.giaTheoNgay,
        category: "Hotel",
      })),
  },
]);
</script>
