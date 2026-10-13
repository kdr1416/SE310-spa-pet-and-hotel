<template>
  <section class="customer-hero page grid items-center gap-10 lg:grid-cols-[.92fr_1.08fr]">
    <div>
      <p class="eyebrow mb-6">Dành cho những người bạn nhỏ</p>
      <h1>
        Chăm sóc thật dịu dàng.<br /><span class="customer-highlight"
          >Đồng hành thật yên tâm.</span
        >
      </h1>
      <p class="muted mt-6 max-w-md text-base">
        Từ một buổi spa thư giãn đến kỳ nghỉ ấm áp, mỗi bé được chăm sóc theo
        cách riêng — như ở nhà.
      </p>
      <div class="mt-8 flex flex-wrap gap-3">
        <RouterLink to="/booking" class="btn-accent"
          >Đặt lịch ngay
          <ArrowRight :size="18" aria-hidden="true" /></RouterLink
        ><RouterLink to="/tracking" class="btn"
          ><Search :size="18" aria-hidden="true" />Tra cứu lịch hẹn</RouterLink
        >
      </div>
      <div
        class="mt-8 flex flex-wrap gap-5 border-t border-primary-200 pt-6 text-sm font-bold"
      >
        <span class="flex gap-2"
          ><Heart :size="18" aria-hidden="true" />Chăm sóc nhẹ nhàng</span
        ><span class="flex gap-2"
          ><ShieldCheck :size="18" aria-hidden="true" />Theo dõi từng bé</span
        >
      </div>
    </div>
    <div
      class="relative h-[360px] overflow-hidden rounded-[28px] bg-primary-100 shadow-modal md:h-[440px]"
    >
      <StoredPhoto
        blob="https://kombai-assets.b-cdn.net/generated_assets/9a265b3efd3e4452867970f49de59adf.jpg"
        alt="Chú chó thư giãn trong bồn tắm tại spa"
      />
      <div class="absolute bottom-5 left-5 right-5 rounded-2xl bg-white/95 p-4">
        <p class="font-heading">Một ngày thật vui</p>
        <p class="muted">
          Tiến độ và nhật ký chăm sóc trong cùng một lịch hẹn.
        </p>
      </div>
    </div>
  </section>
  <section class="border-y border-primary-100 bg-white">
    <div class="page">
      <div class="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p class="eyebrow">Dịch vụ của chúng mình</p>
          <h2 class="mt-2">Chọn điều bé cần</h2>
        </div>
        <RouterLink to="/services" class="font-bold text-primary-700"
          >Xem tất cả dịch vụ →</RouterLink
        >
      </div>
      <ActionFeedback :error="error" :busy="busy" />
      <div class="grid gap-5 md:grid-cols-3">
        <ServiceCard
          v-for="(item, i) in cards"
          :key="item.title"
          :item="item"
          :image="images[i]"
          @detail="$router.push('/services')"
        />
      </div>
    </div>
  </section>
  <section class="page">
    <div
      class="promo-panel grid gap-6 rounded-2xl bg-slate-900 p-8 text-white md:grid-cols-[1fr_auto] md:items-center"
    >
      <div>
        <h2>Một lần đặt, đủ điều bé cần.</h2>
        <p class="mt-3 max-w-xl text-sm text-slate-200">
          Spa, lưu trú hoặc cả hai cho từng bé. Giá lúc đặt chỉ là tạm tính;
          nhân viên chốt sau khi tiếp nhận.
        </p>
      </div>
      <RouterLink to="/booking" class="btn-accent"
        >Đặt lịch cho các bé <ArrowRight :size="18"
      /></RouterLink>
    </div>
  </section>
</template>
<script setup>
import { computed, onMounted } from "vue";
import { ArrowRight, Search, Heart, ShieldCheck } from "lucide-vue-next";
import { useWorkspaceStore } from "@/stores/workspace";
import { useAction } from "@/composables/useAction";
import StoredPhoto from "@/components/shared/StoredPhoto.vue";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";
import ServiceCard from "@/components/public/ServiceCard.vue";
const store = useWorkspaceStore(),
  { busy, error, perform } = useAction();
onMounted(() => perform(() => store.loadCatalog()));
const images = [
  "https://images.pexels.com/photos/19145882/pexels-photo-19145882.jpeg?auto=compress&cs=tinysrgb&w=720",
  "https://images.pexels.com/photos/406014/pexels-photo-406014.jpeg?auto=compress&cs=tinysrgb&w=720",
  "https://images.pexels.com/photos/4587993/pexels-photo-4587993.jpeg?auto=compress&cs=tinysrgb&w=720",
];
const cards = computed(() => {
  const c = store.catalog;
  return [
    c.services[0] && {
      title: c.services[0].tenDichVu,
      description: c.services[0].moTa,
      price: c.services[0].giaCoBan,
      category: "Spa",
    },
    c.rooms[0] && {
      title: c.rooms[0].tenPhong,
      description: "Phòng riêng, ngày nhận/trả và giấy tiêm chủng cho từng bé.",
      price: c.rooms[0].giaTheoNgay,
      category: "Hotel",
    },
    c.combos[0] && {
      title: c.combos[0].tenCombo,
      description: c.combos[0].moTa,
      price: c.combos[0].giaCombo,
      category: "Combo",
    },
  ].filter(Boolean);
});
</script>
