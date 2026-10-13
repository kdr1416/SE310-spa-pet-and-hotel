import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import CustomerLayout from "@/layouts/CustomerLayout.vue";
import AppLayout from "@/layouts/AppLayout.vue";
const routes = [
  {
    path: "/",
    component: CustomerLayout,
    children: [
      {
        path: "",
        component: () => import("@/views/customer/CustomerHomeView.vue"),
        meta: { title: "Trang chủ" },
      },
      {
        path: "services",
        component: () => import("@/views/customer/CustomerServicesView.vue"),
        meta: { title: "Dịch vụ" },
      },
      {
        path: "about",
        component: () => import("@/views/customer/CustomerAboutView.vue"),
        meta: { title: "Về chúng mình" },
      },
      {
        path: "booking",
        component: () => import("@/views/customer/CustomerBookingView.vue"),
        meta: { title: "Đặt lịch" },
      },
      {
        path: "tracking",
        component: () => import("@/views/customer/CustomerTrackingView.vue"),
        meta: { title: "Tra cứu" },
      },
    ],
  },
  {
    path: "/login",
    component: () => import("@/views/auth/LoginView.vue"),
    meta: { guestOnly: true, title: "Đăng nhập" },
  },
  {
    path: "/staff",
    component: AppLayout,
    meta: { requiresAuth: true },
    children: [
      { path: "", redirect: "/staff/overview" },
      {
        path: "overview",
        component: () => import("@/views/staff/StaffOverviewView.vue"),
        meta: { capability: "BOOKING_READ", title: "Tổng quan" },
      },
      {
        path: "dashboard",
        component: () => import("@/views/staff/StaffDashboardView.vue"),
        meta: { capability: "SCHEDULE_SELF", title: "Lịch làm việc của bạn" },
      },
      {
        path: "bookings",
        component: () => import("@/views/staff/StaffBookingsView.vue"),
        meta: { capability: "BOOKING_READ", title: "Danh sách lịch hẹn" },
      },
      {
        path: "care-tasks",
        component: () => import("@/views/staff/StaffCareTasksView.vue"),
        meta: { capability: "CARE_UPDATE", title: "Thực hiện dịch vụ" },
      },
      {
        path: "settings",
        component: () => import("@/views/shared/AccountSettingsView.vue"),
        meta: { title: "Cài đặt tài khoản" },
      },
      {
        path: "check-in",
        redirect: (to) => ({ path: "/staff/bookings", query: to.query }),
      },
      { path: "cashier", redirect: "/staff/bookings" },
      { path: "schedule", redirect: "/staff/dashboard" },
    ],
  },
  {
    path: "/admin",
    component: AppLayout,
    meta: { requiresAuth: true, roles: ["ADMIN"] },
    children: [
      { path: "", redirect: "/admin/overview" },
      {
        path: "overview",
        component: () => import("@/views/admin/AdminOverviewView.vue"),
        meta: { capability: "BOOKING_READ", title: "Tổng quan" },
      },
      {
        path: "dashboard",
        component: () => import("@/views/admin/AdminDashboardView.vue"),
        meta: { capability: "FINANCE_MANAGE", title: "Thống kê" },
      },
      {
        path: "bookings",
        component: () => import("@/views/admin/AdminBookingsView.vue"),
        meta: { capability: "BOOKING_MANAGE", title: "Lịch booking" },
      },
      {
        path: "staff-schedule",
        component: () => import("@/views/admin/AdminStaffScheduleView.vue"),
        meta: { capability: "STAFF_MANAGE", title: "Lịch làm việc nhân viên" },
      },
      {
        path: "finance",
        component: () => import("@/views/admin/AdminFinanceView.vue"),
        meta: { capability: "FINANCE_MANAGE", title: "Tài chính & khiếu nại" },
      },
      {
        path: "staff",
        component: () => import("@/views/admin/AdminStaffView.vue"),
        meta: { capability: "STAFF_MANAGE", title: "Quản lý nhân viên" },
      },
      {
        path: "settings",
        component: () => import("@/views/admin/AdminSettingsView.vue"),
        meta: { capability: "SYSTEM_MANAGE", title: "Cài đặt hệ thống" },
      },
      {
        path: "account",
        component: () => import("@/views/shared/AccountSettingsView.vue"),
        meta: { title: "Tài khoản của bạn" },
      },
      { path: "services", redirect: "/admin/finance" },
      { path: "shifts-slots", redirect: "/admin/settings" },
    ],
  },
  {
    path: "/:pathMatch(.*)*",
    component: () => import("@/views/NotFoundView.vue"),
    meta: { title: "Không tìm thấy trang" },
  },
];
const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
});
router.beforeEach(async (to) => {
  const auth = useAuthStore();
  await auth.initAuth();
  document.title = (to.meta.title || "PetSpa") + " · PetSpa & Hotel";
  if (to.meta.requiresAuth && !auth.isAuthenticated)
    return { path: "/login", query: { redirect: to.fullPath } };
  const home = auth.isAdmin ? "/admin/overview" : "/staff/overview";
  if (to.meta.guestOnly && auth.isAuthenticated) return home;
  if (
    (to.meta.roles && !to.meta.roles.includes(auth.user?.vaiTro)) ||
    (to.meta.capability && !auth.can(to.meta.capability))
  )
    return home;
});
export default router;
