<template>
  <section class="page grid gap-8 md:grid-cols-2">
    <div>
      <p class="eyebrow">Về chúng mình</p>
      <h1 class="mt-4">Chăm sóc tử tế.<br />Luôn gần bên bạn.</h1>
      <p class="muted mt-6">
        PetSpa & Hotel là đồ án hệ thống quản lý spa và lưu trú thú cưng. Cổng
        khách hàng giúp đặt lịch và theo dõi từng bé bằng nhật ký chăm sóc.
      </p>
    </div>
    <div class="panel space-y-6">
      <h2>Thông tin liên hệ</h2>
      <p class="muted">
        Địa chỉ và hotline chính thức chưa được nhóm xác nhận. Thông tin trong
        bản HTML là dữ liệu minh họa.
      </p>
      <div>
        <h3>Giờ ca trong bản demo</h3>
        <ul class="mt-3 space-y-2 text-sm">
          <li v-for="s in store.catalog.shifts" :key="s.id">
            {{ s.tenCa }} · {{ s.gioBatDau }}–{{ s.gioKetThuc }}
          </li>
        </ul>
      </div>
      <RouterLink to="/booking" class="btn-accent">Đặt lịch</RouterLink
      ><ActionFeedback :error="error" />
    </div>
  </section>
</template>
<script setup>
import { onMounted } from "vue";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAction } from "@/composables/useAction";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
const store = useWorkspaceStore(),
  { error, perform } = useAction();
onMounted(() => perform(() => store.loadCatalog()));
</script>
