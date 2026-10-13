<template>
  <div class="relative min-h-screen overflow-hidden bg-warm-page flex items-center justify-center p-4 sm:p-8">
    <!-- Ambient decorative blobs -->
    <div class="pointer-events-none absolute -top-32 -left-32 h-96 w-96 rounded-full bg-primary-300/15 blur-3xl" aria-hidden="true"></div>
    <div class="pointer-events-none absolute -bottom-32 -right-32 h-96 w-96 rounded-full bg-amber-300/15 blur-3xl" aria-hidden="true"></div>

    <main class="relative z-10 w-full max-w-[1180px] grid items-center gap-10 lg:grid-cols-[1.1fr_0.9fr] lg:gap-14">
      <!-- Left Hero Column -->
      <div class="flex flex-col justify-between py-4">
        <div>
          <!-- Brand Logo Link -->
          <RouterLink
            to="/"
            class="group inline-flex items-center gap-3 transition-transform active:scale-98"
            aria-label="Về trang chủ PetSpa & Hotel"
          >
            <span class="brand-squircle h-12 w-12 group-hover:scale-105">
              <PawPrint :size="24" aria-hidden="true" />
            </span>
            <div>
              <span class="font-heading text-2xl font-black text-slate-900 leading-tight">
                PetSpa <span class="text-primary">&</span> Hotel
              </span>
              <span class="block text-[10.5px] font-bold uppercase tracking-[.18em] text-stone-500">
                Cổng Tác Nghiệp Nội Bộ
              </span>
            </div>
          </RouterLink>

          <!-- Main Headlines -->
          <div class="mt-8 max-w-lg">
            <div class="inline-flex items-center gap-2 rounded-full border border-primary-200 bg-primary-50 px-3.5 py-1 text-xs font-bold text-primary-900 shadow-2xs">
              <Sparkles :size="13" class="text-primary-500" />
              <span>Không gian làm việc nhân viên & quản trị</span>
            </div>
            <h1 class="mt-4 font-heading text-4xl font-black leading-[1.2] text-slate-900 md:text-5xl">
              Mỗi ca làm việc,
              <span class="block bg-sunset bg-clip-text text-transparent">
                một ngày chăm sóc tốt hơn.
              </span>
            </h1>
            <p class="mt-4 text-base leading-relaxed text-stone-600 font-medium">
              Không gian tác nghiệp chuẩn mực cho lễ tân quầy, nhân viên kỹ thuật spa và người quản trị hệ thống.
            </p>
          </div>

          <!-- Feature Cards -->
          <div class="mt-8 grid grid-cols-1 gap-3 sm:grid-cols-3 max-w-lg">
            <div class="rounded-2xl border border-primary-200 bg-white/80 p-3.5 shadow-2xs backdrop-blur-xs">
              <div class="flex items-center gap-2 text-xs font-bold text-primary-900">
                <span class="grid h-7 w-7 place-items-center rounded-xl bg-pastel-orange text-primary-600">
                  <Store :size="15" />
                </span>
                <span>Lễ tân quầy</span>
              </div>
              <p class="mt-1 text-[11px] text-stone-500">Tiếp nhận, xếp ca và bàn giao thú cưng</p>
            </div>

            <div class="rounded-2xl border border-amber-200 bg-white/80 p-3.5 shadow-2xs backdrop-blur-xs">
              <div class="flex items-center gap-2 text-xs font-bold text-amber-900">
                <span class="grid h-7 w-7 place-items-center rounded-xl bg-pastel-amber text-amber-700">
                  <HeartHandshake :size="15" />
                </span>
                <span>Kỹ thuật viên</span>
              </div>
              <p class="mt-1 text-[11px] text-stone-500">Nhật ký chăm sóc & ảnh trực tiếp</p>
            </div>

            <div class="rounded-2xl border border-emerald-200 bg-white/80 p-3.5 shadow-2xs backdrop-blur-xs">
              <div class="flex items-center gap-2 text-xs font-bold text-emerald-900">
                <span class="grid h-7 w-7 place-items-center rounded-xl bg-pastel-emerald text-emerald-700">
                  <ShieldCheck :size="15" />
                </span>
                <span>Quản trị viên</span>
              </div>
              <p class="mt-1 text-[11px] text-stone-500">Doanh thu, ca làm & phân quyền</p>
            </div>
          </div>
        </div>

        <!-- Left Column Footer -->
        <div class="mt-8 pt-5 border-t border-app-border text-xs text-stone-500 flex items-center justify-between">
          <span>© 2026 PetSpa & Hotel</span>
          <RouterLink to="/" class="font-bold text-primary-700 hover:text-primary-800 hover:underline">
            Xem trang khách hàng →
          </RouterLink>
        </div>
      </div>

      <!-- Right Login Card -->
      <section class="rounded-3xl border border-primary-200 bg-white p-6 sm:p-8 shadow-warm-lg">
        <!-- Card Header -->
        <div class="flex items-center justify-between gap-3">
          <div>
            <h2 class="font-heading text-2xl font-black text-slate-900">
              Đăng nhập nội bộ
            </h2>
            <p class="mt-1 text-xs font-medium text-stone-500">
              Truy cập không gian làm việc của bạn
            </p>
          </div>
          <span class="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-pastel-orange text-primary-600 border border-primary-200 shadow-2xs">
            <Lock :size="20" aria-hidden="true" />
          </span>
        </div>

        <!-- Mock Demo Quick Switcher -->
        <div v-if="mock" class="mt-5 rounded-2xl border border-amber-200 bg-gradient-to-br from-pastel-amber to-amber-50/50 p-4 shadow-2xs">
          <div class="flex items-center justify-between gap-2">
            <p class="text-xs font-bold text-amber-950 flex items-center gap-1.5">
              <Sparkles :size="13" class="text-amber-600" />
              Tài khoản mẫu nhanh (Mock)
            </p>
            <span class="text-[10px] font-bold text-amber-800 bg-amber-100 px-2 py-0.5 rounded-full">1-Click</span>
          </div>
          <p class="mt-1 text-[11px] text-stone-600 leading-relaxed">
            Chọn nhanh vai trò để đăng nhập thử nghiệm:
          </p>
          <div class="mt-3 grid grid-cols-3 gap-2">
            <button
              v-for="demo in demos"
              :key="demo.id"
              type="button"
              class="group flex flex-col items-center justify-center gap-1 rounded-xl border border-app-border bg-white py-2.5 px-2 text-xs font-bold text-slate-700 shadow-2xs transition-all hover:border-primary-300 hover:bg-pastel-orange hover:text-primary-700 hover:-translate-y-0.5 active:translate-y-0"
              :disabled="auth.loading"
              @click="enterDemo(demo.id)"
            >
              <component :is="demo.icon" :size="16" class="text-stone-400 group-hover:text-primary-600 transition-colors" />
              <span class="font-heading font-extrabold">{{ demo.label }}</span>
              <span class="text-[10px] font-medium text-stone-400 group-hover:text-primary-700">{{ demo.role }}</span>
            </button>
          </div>
        </div>

        <!-- Login Form -->
        <form class="mt-5 space-y-4" @submit.prevent="login">
          <div>
            <label class="block text-xs font-bold uppercase tracking-wider text-stone-600 mb-1.5" for="login-username">
              Username
            </label>
            <div class="relative">
              <span class="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-stone-400">
                <User :size="17" />
              </span>
              <input
                id="login-username"
                v-model="username"
                class="field pl-10"
                autocomplete="username"
                placeholder="Username (vd: admin, receptionist, spa)"
                required
              />
            </div>
          </div>

          <div>
            <label class="block text-xs font-bold uppercase tracking-wider text-stone-600 mb-1.5" for="login-password">
              Mật khẩu
            </label>
            <div class="relative">
              <span class="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-stone-400">
                <KeyRound :size="17" />
              </span>
              <input
                id="login-password"
                v-model="password"
                class="field pl-10"
                type="password"
                autocomplete="current-password"
                placeholder="••••••••"
                required
              />
            </div>
          </div>

          <p v-if="mock" class="text-[11px] text-stone-500 leading-relaxed">
            Form này dành cho tài khoản mock đã tạo hoặc đổi mật khẩu trong cài đặt.
          </p>

          <button
            type="submit"
            class="btn-primary w-full py-3"
            :disabled="auth.loading"
          >
            <LogIn :size="17" />
            <span>{{ auth.loading ? 'Đang xác thực…' : 'Đăng nhập vào hệ thống' }}</span>
          </button>
        </form>

        <ActionFeedback :error="auth.error" :busy="auth.loading" class="mt-4" />

        <!-- Customer redirect footer -->
        <div class="mt-5 pt-4 border-t border-app-border text-center">
          <RouterLink
            to="/tracking"
            class="inline-flex items-center gap-1.5 text-xs font-bold text-primary-700 hover:text-primary-800 hover:underline"
          >
            <span>Khách hàng tra cứu lịch hẹn tại đây</span>
            <ArrowRight :size="13" />
          </RouterLink>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useRouter, useRoute } from "vue-router";
import {
  PawPrint,
  Sparkles,
  Store,
  HeartHandshake,
  ShieldCheck,
  Lock,
  User,
  KeyRound,
  LogIn,
  ArrowRight,
} from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";
import { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import ActionFeedback from "@/components/shared/ActionFeedback.vue";

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const username = ref("");
const password = ref("");
const mock = IS_MOCK_ENABLED;

const demos = [
  { id: 2, label: "Quầy", icon: Store, role: "Lễ tân" },
  { id: 3, label: "Spa", icon: HeartHandshake, role: "Chăm sóc" },
  { id: 1, label: "Admin", icon: ShieldCheck, role: "Quản trị" },
];

function redirect() {
  const next = route.query.redirect;
  router.push(
    typeof next === "string" && next.startsWith("/") && !next.startsWith("//")
      ? next
      : auth.isAdmin
        ? "/admin/overview"
        : "/staff/overview",
  );
}

async function login() {
  if ((await auth.login(username.value, password.value)).success) redirect();
  password.value = "";
}

async function enterDemo(id) {
  if ((await auth.demoLogin(id)).success) redirect();
}
</script>
