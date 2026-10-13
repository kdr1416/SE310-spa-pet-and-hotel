<template>
  <div class="min-h-screen bg-app-bg">
    <header class="no-print sticky top-0 z-30 border-b border-app-border bg-white/95 backdrop-blur-md shadow-warm-sm">
      <div class="flex flex-wrap items-center justify-between gap-3 px-4 py-3 lg:px-8">
        <!-- Brand identity -->
        <div class="flex items-center gap-3">
          <RouterLink
            to="/"
            class="group flex items-center gap-2.5 transition-transform active:scale-98"
            aria-label="Trang chủ PetSpa"
          >
            <span class="brand-squircle h-10 w-10 shrink-0 group-hover:scale-105">
              <PawPrint :size="20" aria-hidden="true" />
            </span>
            <div>
              <span class="font-heading text-lg font-black text-slate-900 transition-colors group-hover:text-primary-600">
                PetSpa <span class="text-primary-500">&</span> Hotel
              </span>
              <span class="block text-[9.5px] font-extrabold uppercase tracking-widest text-stone-400">
                Workspace Hub
              </span>
            </div>
          </RouterLink>
          <span class="hidden sm:inline-flex items-center rounded-full border border-primary-200 bg-primary-50 px-2.5 py-0.5 text-[10px] font-extrabold text-primary-800 shadow-2xs">
            Nội bộ
          </span>
        </div>

        <!-- Right actions -->
        <div class="flex items-center gap-2.5">
          <!-- Quick link to view customer website -->
          <RouterLink
            to="/"
            target="_blank"
            class="hidden items-center gap-1.5 rounded-xl border border-app-border bg-white px-3 py-1.5 text-xs font-bold text-stone-600 shadow-2xs transition-all hover:bg-app-subtle hover:text-primary-700 md:inline-flex"
            title="Mở website khách hàng trong tab mới"
          >
            <ExternalLink :size="14" class="text-stone-400" aria-hidden="true" />
            <span>Trang khách</span>
          </RouterLink>

          <!-- User profile pill -->
          <div class="inline-flex items-center gap-2.5 rounded-2xl border border-primary-200 bg-gradient-to-r from-pastel-orange to-white px-3.5 py-1.5 shadow-2xs">
            <span class="relative flex h-2 w-2">
              <span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75"></span>
              <span class="relative inline-flex h-2 w-2 rounded-full bg-emerald-500"></span>
            </span>
            <span class="font-heading text-xs font-black text-slate-800">{{ auth.userName }}</span>
            <span class="rounded-full bg-primary-500 px-2.5 py-0.5 text-[10px] font-black uppercase tracking-wider text-white shadow-2xs">
              {{ auth.userRoleLabel }}
            </span>
          </div>

          <!-- Logout button -->
          <button
            class="inline-flex items-center gap-1.5 rounded-xl border border-stone-200 bg-white px-3 py-1.5 text-xs font-bold text-stone-600 shadow-2xs transition-all hover:border-red-200 hover:bg-red-50 hover:text-red-700"
            @click="logout"
            title="Đăng xuất khỏi hệ thống"
          >
            <LogOut :size="14" aria-hidden="true" />
            <span class="hidden sm:inline">Đăng xuất</span>
          </button>
        </div>
      </div>
    </header>

    <div class="mx-auto grid max-w-[1600px] lg:grid-cols-[256px_1fr]">
      <aside
        class="no-print border-b border-app-border bg-white p-4 lg:min-h-[calc(100vh-65px)] lg:border-r"
      >
        <nav
          class="flex flex-wrap gap-1.5 lg:flex-col"
          aria-label="Điều hướng nội bộ"
        >
          <div v-for="(group, gIdx) in menuGroups" :key="group.title" class="w-full">
            <p
              class="hidden lg:block px-3 pt-3 pb-1.5 text-[10.5px] font-black uppercase tracking-wider text-stone-400 font-heading"
              :class="{ '!pt-1': gIdx === 0 }"
            >
              {{ group.title }}
            </p>
            <div class="flex flex-wrap gap-1.5 lg:flex-col">
              <RouterLink
                v-for="item in group.items"
                :key="item.to"
                :to="item.to"
                class="group flex items-center gap-2.5 rounded-xl px-3.5 py-2.5 text-sm transition-all duration-150"
                :class="
                  isRouteActive(item.to)
                    ? 'bg-sunset text-white font-black shadow-warm-md'
                    : 'text-stone-600 hover:bg-primary-50/70 hover:text-primary-700 hover:translate-x-1 font-semibold'
                "
              >
                <component
                  :is="item.icon"
                  :size="18"
                  class="shrink-0 transition-colors"
                  :class="isRouteActive(item.to) ? 'text-white' : 'text-stone-400 group-hover:text-primary-500'"
                  aria-hidden="true"
                />
                <span>{{ item.label }}</span>
              </RouterLink>
            </div>
          </div>
        </nav>

        <div class="mt-8 rounded-2xl border border-primary-200 bg-gradient-to-br from-pastel-orange to-primary-100/50 p-3.5 text-xs text-stone-600 shadow-2xs">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2 font-bold text-primary-950">
              <span class="brand-squircle h-6 w-6">
                <PawPrint :size="13" />
              </span>
              <span>PetSpa Hub</span>
            </div>
            <span class="flex h-2 w-2 rounded-full bg-emerald-500" title="Hệ thống sẵn sàng"></span>
          </div>
          <p class="mt-2 leading-relaxed text-[11px] font-medium text-stone-600">
            Dữ liệu mock lưu trong trình duyệt.<br />Nhóm quầy/spa là phương án tạm; role vẫn ADMIN/STAFF.
          </p>
        </div>
      </aside>

      <main id="main-content" class="min-w-0 p-4 md:p-8">
        <div class="mb-6">
          <span class="inline-flex items-center gap-1.5 rounded-full border border-primary-200 bg-primary-50 px-3.5 py-1 text-xs font-bold uppercase tracking-wider text-primary-900 shadow-2xs">
            <PawPrint :size="12" class="text-primary-600" />
            Không gian làm việc
          </span>
          <h1 class="mt-2.5 font-heading text-2xl font-black text-slate-900 md:text-3xl">{{ route.meta.title }}</h1>
        </div>
        <RouterView />
      </main>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  PawPrint,
  CalendarDays,
  HeartHandshake,
  BarChart3,
  Users,
  Receipt,
  Settings,
  LayoutDashboard,
  ExternalLink,
  LogOut,
  User,
} from "lucide-vue-next";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const menuGroups = computed(() => {
  if (auth.isAdmin) {
    return [
      {
        title: "Vận hành & Lịch",
        items: [
          { to: "/admin/overview", label: "Tổng quan", icon: LayoutDashboard },
          { to: "/admin/dashboard", label: "Thống kê", icon: BarChart3 },
          { to: "/admin/bookings", label: "Lịch booking", icon: CalendarDays },
          { to: "/admin/staff-schedule", label: "Lịch nhân viên", icon: CalendarDays },
        ],
      },
      {
        title: "Quản trị & Hệ thống",
        items: [
          { to: "/admin/finance", label: "Tài chính & khiếu nại", icon: Receipt },
          { to: "/admin/staff", label: "Quản lý nhân viên", icon: Users },
          { to: "/admin/settings", label: "Cài đặt hệ thống", icon: Settings },
          { to: "/admin/account", label: "Tài khoản", icon: User },
        ],
      },
    ];
  }
  return [
    {
      title: "Công việc trong ngày",
      items: [
        { to: "/staff/overview", label: "Tổng quan", icon: LayoutDashboard },
        { to: "/staff/bookings", label: "Danh sách lịch hẹn", icon: CalendarDays },
        ...(auth.can("CARE_UPDATE")
          ? [{ to: "/staff/care-tasks", label: "Thực hiện dịch vụ", icon: HeartHandshake }]
          : []),
        { to: "/staff/dashboard", label: "Lịch làm việc", icon: CalendarDays },
      ],
    },
    {
      title: "Cá nhân",
      items: [
        { to: "/staff/settings", label: "Tài khoản", icon: User },
      ],
    },
  ];
});

function isRouteActive(targetPath) {
  if (route.path === targetPath) return true;
  if (targetPath !== "/" && route.path.startsWith(targetPath + "/")) return true;
  return false;
}

function logout() {
  auth.logout();
  router.push("/login");
}
</script>
