import { defineStore } from "pinia";
import httpClient, { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import { mockCall, transaction } from "@/services/mock/mockRepository";
import { matchesMockPassword } from "@/utils/mockCredentials";
import { canAccess } from "@/utils/permissions";
export const useAuthStore = defineStore("auth", {
  state: () => ({ token: null, user: null, loading: false, error: null }),
  getters: {
    isAuthenticated: (s) =>
      !!s.token &&
      ["ADMIN", "STAFF"].includes(s.user?.vaiTro) &&
      s.user?.isActive !== false,
    isAdmin: (s) => s.user?.vaiTro === "ADMIN",
    isStaff: (s) => s.user?.vaiTro === "STAFF",
    currentUser: (s) => s.user,
    userName: (s) => s.user?.tenNhanVien || "Nhân viên",
    userRoleLabel: (s) =>
      s.user?.vaiTro === "ADMIN"
        ? "Quản trị viên"
        : s.user?.workGroup === "SPA"
          ? "Nhân viên spa"
          : "Nhân viên quầy",
  },
  actions: {
    can(capability) {
      return canAccess(this.user, capability);
    },
    async demoLogin(id) {
      this.loading = true;
      this.error = null;
      try {
        if (!IS_MOCK_ENABLED)
          throw new Error("Demo chỉ dùng khi mock được bật.");
        this.user = await mockCall("demoUser", { id });
        this.token = "mock-session";
        sessionStorage.setItem(
          "petspa_mock_session",
          JSON.stringify({ id: this.user.id }),
        );
        return { success: true };
      } catch (e) {
        this.error = e.message;
        return { success: false, message: e.message };
      } finally {
        this.loading = false;
      }
    },
    async login(tenDangNhap, matKhau) {
      this.loading = true;
      this.error = null;
      try {
        if (IS_MOCK_ENABLED) {
          const user = await transaction((s) =>
            s.users.find((u) => u.tenDangNhap === tenDangNhap && u.isActive),
          );
          if (!user || !(await matchesMockPassword(matKhau, user.credential)))
            throw new Error(
              "Tên đăng nhập hoặc mật khẩu không chính xác. Tài khoản mẫu dùng nút vào demo.",
            );
          const { credential, ...safeUser } = user;
          this.user = safeUser;
          this.token = "mock-session";
          sessionStorage.setItem(
            "petspa_mock_session",
            JSON.stringify({ id: user.id }),
          );
        } else {
          const res = await httpClient.post("/auth/login", {
            tenDangNhap,
            matKhau,
          });
          this.user = res.data.user;
          this.token = res.data.token;
          localStorage.setItem("petspa_auth_token", this.token);
          localStorage.setItem("petspa_auth_user", JSON.stringify(this.user));
        }
        return { success: true, user: this.user };
      } catch (e) {
        this.logout();
        this.error = e.message;
        return { success: false, message: e.message };
      } finally {
        this.loading = false;
      }
    },
    async initAuth() {
      try {
        if (IS_MOCK_ENABLED) {
          const session = JSON.parse(
            sessionStorage.getItem("petspa_mock_session") || "null",
          );
          this.user = session
            ? await mockCall("currentUser", {}, session.id)
            : null;
          this.token = this.user ? "mock-session" : null;
        } else {
          this.token = localStorage.getItem("petspa_auth_token");
          this.user = JSON.parse(
            localStorage.getItem("petspa_auth_user") || "null",
          );
        }
      } catch {
        this.logout();
      }
    },
    logout() {
      this.user = null;
      this.token = null;
      sessionStorage.removeItem("petspa_mock_session");
      localStorage.removeItem("petspa_auth_token");
      localStorage.removeItem("petspa_auth_user");
    },
  },
});
