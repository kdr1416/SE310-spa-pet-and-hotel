// @vitest-environment jsdom
import { AxiosError } from "axios";
import { createPinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import httpClient from "../../src/services/api/httpClient";

describe("transport", () => {
  const backendEnvelope = {
    success: true,
    data: { maChiTietDon: 17 },
    message: "Đã tiếp nhận thú cưng",
  };

  function respondWith(data, status = 200) {
    return vi.fn(async (config) => ({
      config,
      data,
      status,
      statusText: "",
      headers: {},
    }));
  }

  function rejectWith(status, data) {
    return async (config) => {
      const response = { config, data, status, statusText: "", headers: {} };
      // A custom Axios adapter must reject failed HTTP responses explicitly.
      throw new AxiosError(
        `Request failed with status code ${status}`,
        status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST,
        config,
        {},
        response,
      );
    };
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it("unwraps the Axios response once and keeps the API envelope", async () => {
    const result = await httpClient.get("/catalog", {
      adapter: respondWith(backendEnvelope),
    });

    expect(result).toEqual(backendEnvelope);
    expect(result.data.data).toBeUndefined();
  });

  it("serializes ordinary request bodies as JSON", async () => {
    const adapter = respondWith(backendEnvelope);
    await httpClient.post("/bookings", { maKhungGio: 12 }, { adapter });
    const [config] = adapter.mock.calls[0];

    expect(config.headers.getContentType()).toBe("application/json");
    expect(config.data).toBe('{"maKhungGio":12}');
  });

  it("passes multipart data through without a JSON Content-Type", async () => {
    const form = new FormData();
    const file = new File(["test image"], "care.jpg", { type: "image/jpeg" });
    form.append("anh", file);
    const adapter = respondWith(backendEnvelope, 201);

    await httpClient.post("/internal/booking-pets/17/care-logs", form, { adapter });
    const [config] = adapter.mock.calls[0];

    expect(config.headers.getContentType()).not.toContain("application/json");
    expect(config.data).toBe(form);
    expect(config.data.get("anh")).toBe(file);
  });

  it("uses the stored live token for Bearer authentication", async () => {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 2 }));
    const adapter = respondWith(backendEnvelope);

    await httpClient.get("/internal/bookings", { adapter });
    const [config] = adapter.mock.calls[0];

    expect(config.headers.get("Authorization")).toBe("Bearer test-access-token");
  });

  it("honors explicit Authorization regardless of header casing", async () => {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    const adapter = respondWith(backendEnvelope);

    await httpClient.get("/internal/bookings", {
      adapter,
      headers: { authorization: "Bearer explicit-test-token" },
    });
    const [config] = adapter.mock.calls[0];

    expect(config.headers.get("Authorization")).toBe("Bearer explicit-test-token");
  });

  it.each([
    [400, "VALIDATION_FAILED", "Khối lượng vượt giới hạn", [
      { field: "canNangThucTe", message: "Khối lượng không hợp lệ" },
      { field: "maNhanVien", message: "Chọn nhân viên" },
    ]],
    [403, "FORBIDDEN", "Cần quyền CARE_UPDATE", []],
    [404, "NOT_FOUND", "Không tìm thấy thú cưng", []],
    [409, "CAPACITY_EXCEEDED", "Khung giờ này đã đầy", []],
    [413, "FILE_TOO_LARGE", "Ảnh vượt kích thước tối đa", []],
    [415, "UNSUPPORTED_FILE", "Định dạng ảnh không được hỗ trợ", []],
    [429, "RATE_LIMITED", "Vui lòng chờ trước khi thử lại", []],
    [503, "STORAGE_UNAVAILABLE", "Kho ảnh tạm thời không khả dụng", []],
  ])("preserves the backend envelope for HTTP %i", async (status, errorCode, message, errors) => {
    const envelope = { success: false, data: null, message, errorCode, errors, traceId: "test-trace" };

    await expect(httpClient.post("/bookings", {}, {
      adapter: rejectWith(status, envelope),
    })).rejects.toEqual(envelope);
  });

  it("cleans both session stores before emitting a single unauthorized event", async () => {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    localStorage.setItem("petspa_auth_user", JSON.stringify({ id: 7 }));
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 2 }));
    const listener = vi.fn(() => ({
      token: localStorage.getItem("petspa_auth_token"),
      user: localStorage.getItem("petspa_auth_user"),
      demo: sessionStorage.getItem("petspa_mock_session"),
    }));
    window.addEventListener("auth:unauthorized", listener);

    const envelope = {
      success: false,
      data: null,
      message: "Xác thực đã hết hạn",
      errorCode: "UNAUTHENTICATED",
      errors: [],
      traceId: "test-trace",
    };

    try {
      await expect.soft(httpClient.get("/internal/bookings", {
        adapter: rejectWith(401, envelope),
      })).rejects.toEqual(envelope);

      expect(listener).toHaveBeenCalledTimes(1);
      expect(listener).toHaveReturnedWith({ token: null, user: null, demo: null });
    } finally {
      window.removeEventListener("auth:unauthorized", listener);
    }
  });

  it("normalizes a raw ASP.NET ModelState dictionary", async () => {
    const problem = {
      title: "Dữ liệu không hợp lệ",
      errors: { maKhungGio: ["Chọn khung giờ"], ngayTra: ["Chọn ngày trả"] },
    };

    await expect(httpClient.post("/bookings", {}, {
      adapter: rejectWith(400, problem),
    })).rejects.toMatchObject({
      success: false,
      data: null,
      message: "Dữ liệu không hợp lệ",
      errors: ["Chọn khung giờ", "Chọn ngày trả"],
    });
  });

  it.each([
    [401, "Phiên làm việc đã hết hạn hoặc bạn chưa đăng nhập."],
    [413, "Tệp tải lên vượt quá kích thước cho phép."],
    [415, "Định dạng tệp tải lên không được hỗ trợ."],
    [429, "Có quá nhiều yêu cầu. Vui lòng thử lại sau."],
    [503, "Dịch vụ tạm thời không khả dụng. Vui lòng thử lại sau."],
  ])("provides a fallback for raw HTTP %i", async (status, message) => {
    await expect(httpClient.post("/bookings", {}, {
      adapter: rejectWith(status, "HTTP error"),
    })).rejects.toEqual({ success: false, data: null, message, errors: [] });
  });

  it("reports a network failure when there is no HTTP response", async () => {
    const adapter = async (config) => {
      throw new AxiosError("Network Error", AxiosError.ERR_NETWORK, config, {});
    };

    await expect(httpClient.get("/catalog", { adapter })).rejects.toEqual({
      success: false,
      data: null,
      message: "Không thể kết nối đến máy chủ Backend .NET.",
      errors: ["Network Error / Server Offline"],
    });
  });
});

describe("live session and permissions", () => {
  const employee = {
    maNhanVien: 1,
    tenNhanVien: "Nhân viên thử nghiệm",
    tenDangNhap: "staff-test",
    vaiTro: "STAFF",
    quyen: ["BOOKING_APPROVE"],
    dangHoatDong: true,
  };
  let auth;
  let liveHttpClient;
  let originalAdapter;

  function respondToLogin(session) {
    liveHttpClient.defaults.adapter = async (config) => ({
      config,
      data: { success: true, data: session, message: "Đăng nhập thành công" },
      status: 200,
      statusText: "",
      headers: {},
    });
  }

  function persistUser(user) {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    localStorage.setItem("petspa_auth_user", JSON.stringify(user));
  }

  function expectLoggedOut() {
    expect(auth.isAuthenticated).toBe(false);
    expect(auth.user).toBeNull();
    expect(auth.token).toBeNull();
    expect(localStorage.getItem("petspa_auth_token")).toBeNull();
    expect(localStorage.getItem("petspa_auth_user")).toBeNull();
    expect(sessionStorage.getItem("petspa_mock_session")).toBeNull();
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    vi.stubEnv("VITE_USE_MOCK_API", "false");
    vi.resetModules();
    const { useAuthStore } = await import("../../src/stores/auth");
    ({ default: liveHttpClient } = await import("../../src/services/api/httpClient"));
    originalAdapter = liveHttpClient.defaults.adapter;
    auth = useAuthStore(createPinia());
  });

  afterEach(() => {
    liveHttpClient.defaults.adapter = originalAdapter;
    vi.unstubAllEnvs();
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("stores the canonical identity and an independent demo actor for live staff", async () => {
    respondToLogin({
      accessToken: "test-access-token",
      expiresIn: 3600,
      nhanVien: employee,
      token: "legacy-test-token",
      user: { id: 7, role: "ADMIN" },
    });

    const result = await auth.login("staff-test", "test-password");

    expect(result.success).toBe(true);
    expect(auth.user).toMatchObject({
      ...employee,
      id: 1,
      name: "Nhân viên thử nghiệm",
      role: "STAFF",
      permissions: ["BOOKING_APPROVE"],
      isActive: true,
    });
    expect(auth.isAuthenticated).toBe(true);
    expect(auth.token).toBe("test-access-token");
    expect(localStorage.getItem("petspa_auth_token")).toBe("test-access-token");
    expect(JSON.parse(localStorage.getItem("petspa_auth_user"))).toEqual(auth.user);
    expect(JSON.parse(sessionStorage.getItem("petspa_mock_session"))).toEqual({ id: 2 });
    expect(auth.can("BOOKING_MANAGE")).toBe(true);
    expect(auth.can("CARE_UPDATE")).toBe(false);
  });

  it("normalizes legacy Session and Employee aliases", async () => {
    respondToLogin({
      token: "legacy-test-token",
      expiresIn: 3600,
      user: {
        id: 8,
        name: "Nhân viên cũ",
        tenDangNhap: "legacy-staff",
        role: "STAFF",
        permissions: ["CHECKIN_PERFORM"],
      },
    });

    expect((await auth.login("legacy-staff", "test-password")).success).toBe(true);
    expect(auth.token).toBe("legacy-test-token");
    expect(auth.user).toMatchObject({
      maNhanVien: 8,
      id: 8,
      tenNhanVien: "Nhân viên cũ",
      name: "Nhân viên cũ",
      tenDangNhap: "legacy-staff",
      vaiTro: "STAFF",
      role: "STAFF",
      quyen: ["CHECKIN_PERFORM"],
      permissions: ["CHECKIN_PERFORM"],
      dangHoatDong: true,
      isActive: true,
    });
    expect(auth.can("CHECKIN_PERFORM")).toBe(true);
    expect(auth.can("BOOKING_READ")).toBe(false);
  });

  it("recreates demo storage when a restored live role changes", async () => {
    sessionStorage.setItem("petspa_mock_session", "{invalid demo JSON");
    persistUser({ id: 9, role: "ADMIN", isActive: true });
    await auth.initAuth();

    expect(auth.user).toMatchObject({ maNhanVien: 9, id: 9, vaiTro: "ADMIN" });
    expect(JSON.parse(sessionStorage.getItem("petspa_mock_session"))).toEqual({ id: 1 });

    persistUser(employee);
    await auth.initAuth();

    expect(auth.user).toMatchObject({ maNhanVien: 1, id: 1, vaiTro: "STAFF" });
    expect(JSON.parse(sessionStorage.getItem("petspa_mock_session"))).toEqual({ id: 2 });
    expect(auth.can("SYSTEM_MANAGE")).toBe(false);
  });

  it.each([
    ["corrupt JSON", "{invalid user JSON"],
    ["missing identity", null],
    ["missing employee ID", JSON.stringify({ vaiTro: "STAFF" })],
    ["unsupported role", JSON.stringify({ ...employee, vaiTro: "UNKNOWN" })],
    ["inactive employee", JSON.stringify({ ...employee, dangHoatDong: false, isActive: true })],
  ])("clears a stored session with %s", async (_case, storedUser) => {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    if (storedUser !== null) localStorage.setItem("petspa_auth_user", storedUser);
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 1 }));

    await auth.initAuth();

    expectLoggedOut();
  });

  it("rejects an inactive login even if the legacy isActive alias is true", async () => {
    persistUser(employee);
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 2 }));
    respondToLogin({
      accessToken: "test-inactive-token",
      expiresIn: 3600,
      nhanVien: { ...employee, dangHoatDong: false, isActive: true },
    });

    expect((await auth.login("staff-test", "test-password")).success).toBe(false);
    expectLoggedOut();
  });

  it("clears all identity storage after a rejected login", async () => {
    persistUser(employee);
    await auth.initAuth();
    const failure = { success: false, data: null, message: "Không thể đăng nhập." };
    liveHttpClient.defaults.adapter = async (config) => {
      throw new AxiosError(failure.message, AxiosError.ERR_BAD_REQUEST, config, {}, {
        status: 400,
        data: failure,
        config,
      });
    };

    expect(await auth.login("staff-test", "test-password")).toEqual({
      success: false,
      message: failure.message,
    });
    expectLoggedOut();
  });

  it("clears all identity storage on logout", async () => {
    persistUser(employee);
    await auth.initAuth();

    auth.logout();

    expectLoggedOut();
  });

  it("refreshes in-memory identity after a live 401 clears storage", async () => {
    persistUser(employee);
    await auth.initAuth();
    const failure = { success: false, data: null, message: "Phiên đã hết hạn." };

    await expect(liveHttpClient.get("/internal/bookings", {
      adapter: async (config) => {
        throw new AxiosError(failure.message, AxiosError.ERR_BAD_REQUEST, config, {}, {
          status: 401,
          data: failure,
          config,
        });
      },
    })).rejects.toEqual(failure);
    await auth.initAuth();

    expectLoggedOut();
  });

  it.each([
    ["BOOKING_APPROVE", "BOOKING_READ"],
    ["BOOKING_APPROVE", "BOOKING_MANAGE"],
    ["CARE_UPDATE", "CARE_UPDATE"],
    ["SCHEDULE_REGISTER", "SCHEDULE_SELF"],
    ["INVOICE_CREATE", "FINANCE_MANAGE"],
    ["INVOICE_COLLECT", "FINANCE_MANAGE"],
    ["SCHEDULE_APPROVE", "STAFF_MANAGE"],
    ["PERMISSION_MANAGE", "STAFF_MANAGE"],
    ["SYSTEM_CONFIG", "SYSTEM_MANAGE"],
    ["CHECKIN_PERFORM", "CHECKIN_PERFORM"],
  ])("uses %s for the live %s capability", async (permission, capability) => {
    persistUser({ ...employee, quyen: [permission] });
    await auth.initAuth();

    expect(auth.can(permission)).toBe(true);
    expect(auth.can(capability)).toBe(true);
  });

  it("denies live staff without explicit permissions despite a legacy workGroup", async () => {
    persistUser({ ...employee, quyen: undefined, workGroup: "RECEPTION" });
    await auth.initAuth();

    expect(auth.user).toMatchObject({ quyen: [], permissions: [] });
    expect(auth.can("BOOKING_READ")).toBe(false);
    expect(auth.can("BOOKING_MANAGE")).toBe(false);
    expect(auth.can("SCHEDULE_SELF")).toBe(false);
  });

  it("grants active live admins all UI capabilities without requiring grants", async () => {
    persistUser({ ...employee, vaiTro: "ADMIN", quyen: [] });
    await auth.initAuth();

    expect([
      "BOOKING_READ", "BOOKING_MANAGE", "CARE_UPDATE", "SCHEDULE_SELF",
      "FINANCE_MANAGE", "STAFF_MANAGE", "SYSTEM_MANAGE",
    ].every((capability) => auth.can(capability))).toBe(true);
    expect(auth.can("UNKNOWN_CAPABILITY")).toBe(false);
  });

  it("denies inactive and unsupported actors with explicit live permissions", async () => {
    const { canAccess } = await import("../../src/utils/permissions");

    expect(canAccess({ ...employee, dangHoatDong: false }, "BOOKING_READ")).toBe(false);
    expect(canAccess({ ...employee, vaiTro: "UNKNOWN" }, "BOOKING_APPROVE")).toBe(false);
  });

  it("keeps seed Staff 2 on the mock permission policy in live mode", async () => {
    const { canAccess, requireAccess } = await import("../../src/utils/permissions");
    const { createSeed } = await import("../../src/services/mock/seed");
    const staff = createSeed().users.find((user) => user.id === 2);

    expect(canAccess(staff, "BOOKING_MANAGE")).toBe(true);
    expect(canAccess(staff, "CARE_UPDATE")).toBe(false);
    expect(() => requireAccess(staff, "BOOKING_MANAGE")).not.toThrow();
    expect(() => requireAccess(staff, "CARE_UPDATE")).toThrow();
  });
});
