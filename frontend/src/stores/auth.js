import { defineStore } from "pinia";
import httpClient, { IS_MOCK_ENABLED } from "@/services/api/httpClient";
import { mockCall, transaction } from "@/services/mock/mockRepository";
import { matchesMockPassword } from "@/utils/mockCredentials";
import { canAccess } from "@/utils/permissions";

function normalizeLiveUser(rawUser) {
  const maNhanVien = rawUser?.maNhanVien ?? rawUser?.id;
  if (!Number.isInteger(maNhanVien) || maNhanVien <= 0) return null;

  const tenNhanVien = rawUser.tenNhanVien ?? rawUser.name;
  const vaiTro = rawUser.vaiTro ?? rawUser.role;
  const permissions = rawUser.quyen ?? rawUser.permissions;
  const quyen = Array.isArray(permissions) ? permissions : [];
  const dangHoatDong = rawUser.dangHoatDong ?? rawUser.isActive ?? true;
  return {
    ...rawUser,
    maNhanVien,
    tenNhanVien,
    vaiTro,
    quyen,
    dangHoatDong,
    id: maNhanVien,
    name: tenNhanVien,
    role: vaiTro,
    permissions: quyen,
    isActive: dangHoatDong,
  };
}

function hasValidSession(token, user) {
  return (
    typeof token === "string" &&
    !!token.trim() &&
    ["ADMIN", "STAFF"].includes(user?.vaiTro) &&
    user.isActive !== false
  );
}

function syncDemoSession(user) {
  sessionStorage.setItem(
    "petspa_mock_session",
    JSON.stringify({ id: user.vaiTro === "ADMIN" ? 1 : 2 }),
  );
}

export const useAuthStore = defineStore("auth", {
  state: () => ({ token: null, user: null, loading: false, error: null }),
  getters: {
    isAuthenticated: (s) => hasValidSession(s.token, s.user),
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
          if (!res.success) throw new Error(res.message);

          const token = res.data?.accessToken ?? res.data?.token;
          const user = normalizeLiveUser(res.data?.nhanVien ?? res.data?.user);
          if (!hasValidSession(token, user))
            throw new Error("Phiên đăng nhập không hợp lệ.");

          localStorage.setItem("petspa_auth_token", token);
          localStorage.setItem("petspa_auth_user", JSON.stringify(user));
          syncDemoSession(user);
          this.user = user;
          this.token = token;
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
          const token = localStorage.getItem("petspa_auth_token");
          const user = normalizeLiveUser(
            JSON.parse(localStorage.getItem("petspa_auth_user") || "null"),
          );
          if (!hasValidSession(token, user)) {
            this.logout();
            return;
          }

          syncDemoSession(user);
          this.user = user;
          this.token = token;
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
