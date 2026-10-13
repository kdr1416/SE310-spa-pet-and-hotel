<template>
  <div class="min-h-screen flex bg-slate-50">
    <!-- Left Hero Banner (Hidden on mobile) -->
    <div
      class="hidden lg:flex lg:w-1/2 relative bg-gradient-to-br from-primary-900 via-primary-800 to-amber-950 text-white p-12 flex-col justify-between overflow-hidden"
    >
      <!-- Background pattern / decorative circles -->
      <div
        class="absolute -top-24 -left-24 w-96 h-96 rounded-full bg-primary-600/20 blur-3xl pointer-events-none"
      ></div>
      <div
        class="absolute -bottom-24 -right-24 w-96 h-96 rounded-full bg-amber-500/20 blur-3xl pointer-events-none"
      ></div>

      <!-- Top Branding -->
      <div class="relative z-10 flex items-center gap-3">
        <div
          class="w-12 h-12 rounded-2xl bg-white/10 backdrop-blur-md border border-white/20 flex items-center justify-center text-2xl shadow-inner"
        >
          🐾
        </div>
        <div>
          <span
            class="font-heading font-extrabold text-2xl tracking-tight text-white block"
            >PetSpa & Hotel</span
          >
          <span
            class="text-xs text-amber-200 tracking-wider uppercase font-semibold"
            >Hệ thống Vận hành Thông minh</span
          >
        </div>
      </div>

      <!-- Center Feature Highlights -->
      <div class="relative z-10 max-w-md my-auto space-y-6">
        <div
          class="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-white/10 backdrop-blur-md border border-white/15 text-amber-300 text-xs font-semibold"
        >
          <Sparkles :size="14" />
          <span>Phiên bản đồ án .NET 8 Web API</span>
        </div>

        <h2
          class="text-3xl lg:text-4xl font-heading font-extrabold leading-tight text-white"
        >
          Quản lý quy trình chăm sóc thú cưng chuẩn 5 sao
        </h2>

        <p class="text-sm text-slate-300 leading-relaxed">
          Tối ưu hóa tiếp nhận tại quầy, cập nhật nhật ký ảnh theo thời gian
          thực, quản lý sức chứa slot chống overbooking và chốt hóa đơn snapshot
          an toàn.
        </p>

        <div class="grid grid-cols-2 gap-4 pt-4 border-t border-white/10">
          <div
            class="bg-white/5 backdrop-blur-sm p-4 rounded-xl border border-white/10"
          >
            <div class="text-2xl font-black text-amber-400 font-heading">
              100%
            </div>
            <div class="text-xs text-slate-300 mt-1">
              Minh bạch ảnh chăm sóc cho chủ pet
            </div>
          </div>
          <div
            class="bg-white/5 backdrop-blur-sm p-4 rounded-xl border border-white/10"
          >
            <div class="text-2xl font-black text-emerald-400 font-heading">
              0 Overbook
            </div>
            <div class="text-xs text-slate-300 mt-1">
              Khóa slot bi quan chống trùng giờ
            </div>
          </div>
        </div>
      </div>

      <!-- Footer Quote -->
      <div
        class="relative z-10 text-xs text-slate-400 flex items-center justify-between"
      >
        <span>© 2026 PetSpa & Hotel Management</span>
        <span>SE310 - Công Nghệ .NET</span>
      </div>
    </div>

    <!-- Right Content / Form Viewport -->
    <div class="flex-1 flex items-center justify-center p-6 sm:p-12">
      <div class="w-full max-w-md">
        <slot />
      </div>
    </div>
  </div>
</template>

<script setup>
import { Sparkles } from "lucide-vue-next";
</script>
