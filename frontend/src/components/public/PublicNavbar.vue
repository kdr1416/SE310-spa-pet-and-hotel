<template>
  <header class="no-print sticky top-0 z-40 border-b border-app-border bg-white/95 backdrop-blur-md shadow-warm-sm">
    <div
      class="mx-auto flex max-w-[1240px] items-center justify-between gap-4 px-4 py-3.5 md:px-8"
    >
      <RouterLink
        to="/"
        class="group flex min-w-0 items-center gap-3 transition-transform active:scale-98"
        aria-label="PetSpa & Hotel, trang chủ"
      >
        <span class="brand-squircle h-11 w-11 shrink-0 group-hover:scale-105">
          <PawPrint :size="22" aria-hidden="true" />
        </span>
        <span class="font-heading text-xl font-extrabold text-slate-900 leading-tight">
          PetSpa <span class="text-primary-500">&</span> Hotel
          <span class="block text-[10px] font-sans font-bold uppercase tracking-[.15em] text-stone-500">
            SPA & KHÁCH SẠN THÚ CƯNG
          </span>
        </span>
      </RouterLink>

      <nav
        class="hidden items-center gap-1.5 text-sm font-bold lg:flex"
        aria-label="Điều hướng chính"
      >
        <RouterLink
          v-for="l in links"
          :key="l.to"
          :to="l.to"
          class="rounded-xl px-4 py-2 text-stone-600 transition-all hover:bg-primary-50 hover:text-primary-700"
          exact-active-class="bg-primary-50 text-primary-700 font-extrabold border border-primary-200 shadow-2xs"
        >
          {{ l.label }}
        </RouterLink>
      </nav>

      <div class="flex items-center gap-3">
        <div class="hidden text-right xl:block">
          <span class="block text-[11px] font-semibold text-stone-500">Tư vấn đặt lịch</span>
          <a href="tel:19001234" class="font-heading text-sm font-extrabold text-slate-900 transition-colors hover:text-primary-600">
            1900 1234
          </a>
        </div>
        <RouterLink
          to="/booking"
          class="btn-primary !min-h-0 px-5 py-2.5"
        >
          <CalendarDays :size="17" aria-hidden="true" />
          <span>Đặt lịch ngay</span>
        </RouterLink>
        <button
          class="btn !min-h-10 !px-3 rounded-xl border-stone-200 bg-white lg:hidden"
          :aria-expanded="open"
          aria-controls="mobile-nav"
          aria-label="Mở menu"
          @click="open = !open"
        >
          <Menu :size="20" />
        </button>
      </div>
    </div>

    <nav
      v-if="open"
      id="mobile-nav"
      class="grid gap-2 border-t border-app-border bg-white p-4 shadow-lg lg:hidden"
      aria-label="Điều hướng mobile"
    >
      <RouterLink
        v-for="l in links"
        :key="l.to"
        :to="l.to"
        class="rounded-xl px-4 py-2.5 text-sm font-bold text-slate-700 transition-colors hover:bg-primary-50 hover:text-primary-700"
        exact-active-class="bg-primary-50 text-primary-700 font-extrabold border border-primary-200"
        @click="open = false"
      >
        {{ l.label }}
      </RouterLink>
      <div class="my-1 border-t border-app-border"></div>
      <div class="flex items-center justify-between px-2 py-1 text-xs text-stone-500">
        <span>Tư vấn đặt lịch:</span>
        <a href="tel:19001234" class="font-bold text-primary-600">1900 1234</a>
      </div>
      <RouterLink
        to="/booking"
        class="btn-primary mt-1 w-full py-3"
        @click="open = false"
      >
        <CalendarDays :size="18" />
        <span>Đặt lịch ngay</span>
      </RouterLink>
    </nav>
  </header>
</template>

<script setup>
import { ref } from "vue";
import { PawPrint, CalendarDays, Menu } from "lucide-vue-next";

const open = ref(false);
const links = [
  { to: "/", label: "Trang chủ" },
  { to: "/services", label: "Dịch vụ" },
  { to: "/tracking", label: "Tra cứu" },
  { to: "/about", label: "Về chúng mình" },
];
</script>
