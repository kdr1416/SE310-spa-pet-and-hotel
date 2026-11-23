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

describe("Sprint 1 reads", () => {
  const catalog = {
    dichVu: [{
      maDichVu: 11, tenDichVu: "Tắm", loaiDichVu: "SPA", thoiLuongPhut: 30,
      yeuCauTiemChung: false, bangGia: [{ maSize: "S", donGia: 100000 }, { maSize: "M", donGia: 115000 }],
      moTa: null, dangHoatDong: false,
    }],
    combo: [{
      maCombo: 12, tenCombo: "Combo", thanhPhan: [], bangGia: [{ maSize: "S", donGia: 150000 }],
      moTa: null, dangHoatDong: true,
    }],
    loaiPhong: [{
      maLoaiPhong: 13, tenPhong: "Phòng nhỏ", giaMoiDem: 250000, soLuongPhong: 4,
      dangHoatDong: true,
    }],
    khoangSize: [{ maSize: "S", tuKg: 0, denKg: 5 }],
  };
  const contact = { hoTen: "Khách thử nghiệm", soDienThoai: "0901234567", email: "customer@example.test" };
  const invoice = {
    maHoaDon: 902, soHoaDon: "HD-902", maDon: 101, thayTheHoaDon: null, trangThai: "UNPAID",
    lapAt: "2026-11-23T04:00:00Z", nguoiLap: "Nhân viên thử nghiệm", khach: contact,
    ngayHen: "2026-11-23",
    dongTien: [{
      maDongHoaDon: 30, loaiKhoan: "SPA", maChiTietDon: 15, maDichVuTrongDon: 35, maPhuPhi: null,
      tenPet: "Miu", tenKhoan: "Tắm", size: "M", soLuong: 1, donGia: 115000, thanhTien: 115000,
      dienGiai: null,
    }],
    tongTien: 115000, cocKhauTru: 15000, conPhaiTra: 100000, paidAt: null, thuHoiAt: null, qr: null,
  };
  const booking = {
    maDon: 101, trackingCode: "ABCD1234", trangThai: "IN_PROGRESS",
    batDauDuKien: "2026-11-22T18:00:00Z", ketThucDuKien: "2026-11-23T04:00:00Z",
    lienHe: contact, tongTamTinh: 110000, tienCoc: null, hoaDon: invoice, emailDelivery: "SENT",
    thuCung: [{
      maChiTietDon: 15, maThuCung: 25, tenThuCung: "Miu", trangThai: "READY",
      canNangChot: 6, sizeThucTe: "M", sizeChot: "M", tinhTrangLong: ["BINH_THUONG"],
      maLoaiPhong: null, tenNhanVienPhuTrach: "Nhân viên thử nghiệm",
      dichVu: [{
        maDichVuTrongDon: 35, maDichVu: 11, maCombo: null, maKhungGio: 73, tenDichVu: "Tắm",
        trangThai: "HOAN_TAT", sizeChot: "M", giaChot: 115000, thanhPhan: [],
      }],
      nhatKy: [{
        maNhatKy: 31, loaiNhatKy: "READY", ghiChu: "Đã tắm", anhUrl: "https://example.test/care.jpg",
        canNangTheoDoi: null, tenTacGia: "Nhân viên thử nghiệm", ghiNhanAt: "2026-11-23T03:00:00Z",
      }],
      nhanSom: false, banGiaoAt: null, traLaiAt: null,
    }],
  };
  let callOperation;
  let liveHttpClient;
  let originalAdapter;

  function respondWith(data) {
    const envelope = { success: true, data, message: "Thành công", traceId: "test-read-trace" };
    const adapter = vi.fn(async (config) => ({
      config, data: envelope, status: 200, statusText: "", headers: {},
    }));
    liveHttpClient.defaults.adapter = adapter;
    return { adapter, envelope };
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    vi.stubEnv("VITE_USE_MOCK_API", "false");
    vi.resetModules();
    ({ default: liveHttpClient } = await import("../../src/services/api/httpClient"));
    ({ callOperation } = await import("../../src/services/api/operationClient"));
    originalAdapter = liveHttpClient.defaults.adapter;
  });

  afterEach(() => {
    liveHttpClient.defaults.adapter = originalAdapter;
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("keeps the canonical Catalog envelope and maps display aliases without date-only capacity", async () => {
    const { adapter, envelope } = respondWith(catalog);

    expect(await callOperation("getCatalog", {}, true)).toEqual(envelope);
    const result = await callOperation("catalog", { date: "2026-11-23" }, true);

    expect(adapter).toHaveBeenCalledTimes(2);
    for (const [config] of adapter.mock.calls) {
      expect(config).toMatchObject({ method: "get", url: "/catalog" });
      expect(config.params).toBeUndefined();
    }
    expect(result).toMatchObject({
      ...envelope,
      data: {
        ...catalog,
        services: [{ ...catalog.dichVu[0], id: 11, isActive: false }],
        combos: [{ ...catalog.combo[0], id: 12, isActive: true }],
        rooms: [{ ...catalog.loaiPhong[0], id: 13, isActive: true, giaTheoNgay: 250000 }],
        shifts: [{ tenCa: "Ca Sáng" }, { tenCa: "Ca Chiều" }],
        availability: [],
      },
    });
    expect(result.data.shifts.every((shift) => !("slotPetMacDinh" in shift))).toBe(true);
    expect(catalog.dichVu[0]).not.toHaveProperty("id");
  });

  it("posts Spa demand with the supplied booking and distinct service slot IDs", async () => {
    const availability = { maKhungGio: 73, slotConLai: 3, phutConLai: 70, phong: [], coTheDat: true, lyDo: null };
    const { adapter, envelope } = respondWith(availability);
    const payload = {
      date: "2026-11-23",
      shiftId: 1,
      maKhungGio: 73,
      ngayTra: "2026-11-24",
      thuCung: [
        { tenThuCung: "Miu", dichVu: [{ maDichVu: 11, maKhungGio: 81, shiftId: 2, estimatedPrice: 100000 }] },
        { dichVu: [{ maDichVu: 14, maKhungGio: 82 }] },
      ],
    };
    const original = JSON.parse(JSON.stringify(payload));

    const result = await callOperation("checkAvailability", payload, true);

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "post", url: "/availability" });
    expect(JSON.parse(config.data)).toEqual({
      maKhungGio: 73,
      ngayTra: "2026-11-24",
      thuCung: [
        { dichVu: [{ maDichVu: 11, maKhungGio: 81 }] },
        { dichVu: [{ maDichVu: 14, maKhungGio: 82 }] },
      ],
    });
    expect(payload).toStrictEqual(original);
    expect(result).toEqual(envelope);
  });

  it("rejects availability requested with only a display shift and date", async () => {
    const { adapter } = respondWith(null);

    await expect(callOperation("checkAvailability", {
      shiftId: 1, date: "2026-11-23",
    }, true)).rejects.toMatchObject({
      success: false,
      data: null,
      errorCode: "MISSING_SLOT_MAPPING",
      errors: [{ field: "maKhungGio", message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  });

  it("rejects availability when a service has no slot even if the booking has one", async () => {
    const { adapter } = respondWith(null);
    const payload = {
      maKhungGio: 73,
      ngayTra: "2026-11-24",
      thuCung: [{ dichVu: [{ maDichVu: 11, shiftId: 2 }] }],
    };
    const original = JSON.parse(JSON.stringify(payload));

    await expect(callOperation("checkAvailability", payload, true)).rejects.toMatchObject({
      success: false,
      data: null,
      errorCode: "MISSING_SLOT_MAPPING",
      errors: [{ field: "thuCung[0].dichVu[0].maKhungGio", message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
    expect(payload).toStrictEqual(original);
  });

  it("uses the normalized tracking pair and projects the public-safe result for the UI", async () => {
    const { lienHe, emailDelivery, ...publicBooking } = booking;
    const { adapter, envelope } = respondWith({
      ...publicBooking,
      hoaDon: null,
      hanhDongChoPhep: [],
    });

    expect(await callOperation("lookupTracking", {
      trackingCode: "ABCD1234", soDienThoai: "0901234567", phone: "ignored",
    }, true)).toEqual(envelope);
    const result = await callOperation("lookupBooking", {
      trackingCode: "ABCD1234", phone: "090-123 4567", bookingId: 101,
    }, true);

    expect(adapter).toHaveBeenCalledTimes(2);
    for (const [config] of adapter.mock.calls) {
      expect(config).toMatchObject({ method: "post", url: "/tracking/lookup" });
      expect(JSON.parse(config.data)).toEqual({ trackingCode: "ABCD1234", soDienThoai: "0901234567" });
    }
    expect(result).toMatchObject({
      ...envelope,
      data: {
        ...envelope.data,
        id: 101,
        khachHang: { soDienThoai: "0901234567" },
        date: "2026-11-23",
        chiTietDonList: [{
          id: 15, thuCung: { tenThuCung: "Miu" }, trangThaiChamSoc: "READY",
          nhatKyChamSoc: [{
            id: 31, photo: "https://example.test/care.jpg", ghiChuSucKhoe: "Đã tắm",
            thoiGianGhiNhan: "2026-11-23T03:00:00Z",
          }],
        }],
      },
    });
    expect(result.data.khachHang).not.toHaveProperty("tenKhachHang");
    expect(envelope.data).not.toHaveProperty("chiTietDonList");
  });

  it.each(["090a1234567", "090.123.4567", "+84901234567", "０９０１２３４５６７", 901234567])(
    "rejects unsupported tracking phone input %s before sending HTTP",
    async (phone) => {
      const { adapter } = respondWith(null);

      await expect(callOperation("lookupBooking", { trackingCode: "ABCD1234", phone }, true)).rejects.toMatchObject({
        success: false,
        data: null,
        errorCode: "VALIDATION_FAILED",
        errors: [{ field: "soDienThoai", message: expect.any(String) }],
      });
      expect(adapter).not.toHaveBeenCalled();
    },
  );

  it("passes through PageBooking with the canonical query and authenticates without demo storage", async () => {
    localStorage.setItem("petspa_auth_token", "test-access-token");
    const { adapter, envelope } = respondWith({ items: [booking], trang: 2, kichThuocTrang: 20, tongSo: 25 });

    const result = await callOperation("listBookings", {
      trang: 2, kichThuocTrang: 20, tuNgay: "2026-11-23", denNgay: "2026-11-24",
      trangThai: "IN_PROGRESS", id: 101, actor: 2, search: "Miu",
    });

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "get", url: "/internal/bookings" });
    expect(config.params).toEqual({
      trang: 2, kichThuocTrang: 20, tuNgay: "2026-11-23", denNgay: "2026-11-24", trangThai: "IN_PROGRESS",
    });
    expect(config.headers.get("Authorization")).toBe("Bearer test-access-token");
    expect(result).toEqual(envelope);
  });

  it("projects up to 100 workspace bookings and retains the backend total without emitting updates", async () => {
    const items = Array.from({ length: 100 }, (_, index) => ({ ...booking, maDon: 101 + index }));
    const { adapter } = respondWith({ items, trang: 1, kichThuocTrang: 100, tongSo: 125 });
    const dispatchEvent = vi.spyOn(window, "dispatchEvent");

    const envelope = await callOperation("getWorkspace");

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({
      method: "get", url: "/internal/bookings", params: { kichThuocTrang: 100 },
    });
    expect(envelope).toMatchObject({
      success: true, message: "Thành công", traceId: "test-read-trace",
      data: { users: [], requests: [], trang: 1, kichThuocTrang: 100 },
    });
    expect(envelope.data.tongSo).toBe(125);
    expect(envelope.data.bookings).toHaveLength(100);
    expect(envelope.data.bookings[0]).toMatchObject({
      id: 101, maDon: 101, lienHe: contact,
      date: "2026-11-23", tongTienTamTinh: 110000,
      khachHang: { ...contact, tenKhachHang: "Khách thử nghiệm" },
      hoaDon: {
        ...invoice,
        id: 902, trangThaiThanhToan: "UNPAID", tongThanhToan: 100000,
        ctHoaDonList: [{ id: 30, tenKhoanMuc: "Tắm", thanhTien: 115000 }],
      },
    });
    const projected = envelope.data.bookings[0];
    expect(projected).not.toHaveProperty("maKhungGio");
    expect(projected.chiTietDonList[0]).not.toHaveProperty("nhanVienId");
    expect(projected.chiTietDonList[0].thuCung).not.toHaveProperty("canNangDangKy");
    expect(booking.hoaDon).not.toHaveProperty("id");
    expect(booking.thuCung[0]).not.toHaveProperty("trangThaiChamSoc");
    expect(dispatchEvent).not.toHaveBeenCalled();
    expect(localStorage.getItem("petspa_workspace_update")).toBeNull();
  });

  it("reads the canonical Invoice using the invoice ID when a booking ID is also supplied", async () => {
    const { adapter, envelope } = respondWith(invoice);

    expect(await callOperation("getInvoice", { invoiceId: 902, bookingId: 101 })).toEqual(envelope);
    expect(adapter).toHaveBeenCalledTimes(1);
    expect(adapter.mock.calls[0][0]).toMatchObject({ method: "get", url: "/internal/invoices/902" });
  });

  it("rejects a missing invoice ID without using the booking ID for HTTP", async () => {
    const { adapter } = respondWith(invoice);

    await expect(callOperation("getInvoice", { bookingId: 101 })).rejects.toMatchObject({
      success: false, data: null, errorCode: "VALIDATION_FAILED",
      errors: [{ field: "invoiceId", message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  });

  it("preserves a backend read failure even when mock storage is available", async () => {
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id: 1 }));
    const failure = { success: false, data: null, message: "Cần quyền BOOKING_APPROVE", errorCode: "FORBIDDEN" };
    const adapter = vi.fn(async (config) => {
      throw new AxiosError(failure.message, AxiosError.ERR_BAD_REQUEST, config, {}, {
        config, status: 403, data: failure,
      });
    });
    liveHttpClient.defaults.adapter = adapter;

    await expect(callOperation("getWorkspace")).rejects.toEqual(failure);
    expect(adapter).toHaveBeenCalledTimes(1);
  });
});

describe("commands-booking", () => {
  const contact = { hoTen: "Khách thử nghiệm", soDienThoai: "0901234567", email: "customer@example.test" };
  const booking = {
    maDon: 101, trackingCode: "ABCD1234", trangThai: "PENDING",
    batDauDuKien: "2026-11-23T01:00:00Z", ketThucDuKien: "2026-11-23T01:30:00Z",
    lienHe: contact, tongTamTinh: 110000, tienCoc: null, hoaDon: null, emailDelivery: "SENT",
    thuCung: [{
      maChiTietDon: 15, maThuCung: 25, tenThuCung: "Miu", trangThai: "NOT_ARRIVED",
      canNangChot: null, sizeThucTe: null, sizeChot: null, tinhTrangLong: null,
      maLoaiPhong: null, tenNhanVienPhuTrach: null, dichVu: [{
        maDichVuTrongDon: 35, maDichVu: 11, maCombo: null, maKhungGio: 701,
        tenDichVu: "Tắm", trangThai: "CHO_THUC_HIEN", sizeChot: null, giaChot: null, thanhPhan: [],
      }], nhatKy: [], nhanSom: false, banGiaoAt: null, traLaiAt: null,
    }],
  };
  const requestId = "550e8400-e29b-41d4-a716-446655440000";
  let callOperation;
  let liveHttpClient;
  let originalAdapter;
  let adapter;
  let envelope;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    vi.stubEnv("VITE_USE_MOCK_API", "false");
    vi.resetModules();
    ({ default: liveHttpClient } = await import("../../src/services/api/httpClient"));
    ({ callOperation } = await import("../../src/services/api/operationClient"));
    originalAdapter = liveHttpClient.defaults.adapter;
    envelope = { success: true, data: booking, message: "Thành công", traceId: "test-command-trace" };
    adapter = vi.fn(async (config) => ({ config, data: envelope, status: 200, statusText: "", headers: {} }));
    liveHttpClient.defaults.adapter = adapter;
  });

  afterEach(() => {
    liveHttpClient.defaults.adapter = originalAdapter;
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("posts canonical BookingCreate as multipart and preserves explicit service slots and vaccine parts", async () => {
    const vaccine = new File(["vaccination record"], "vaccine.png", { type: "image/png" });
    const payload = {
      requestId, maKhungGio: 701, shiftId: 1, ngayTra: "2026-11-23", actor: 1,
      date: "2026-11-21", checkout: "2026-11-25",
      lienHe: { ...contact, soDienThoai: "090-123 4567", id: 205 },
      thuCung: [{
        tenThuCung: "Miu", loaiPet: "MEO", canNangKhaiBao: 5, ghiChuDiUng: null,
        vaccinationFileKey: "miu-vaccine", dichVu: [
          { maDichVu: 11, maCombo: null, maKhungGio: 704, estimatedPrice: 10000 },
        ],
      }, {
        tenThuCung: "Mây", loaiPet: "CHO", canNangKhaiBao: 6, dichVu: [{ maDichVu: 12 }],
      }],
      files: { "miu-vaccine": vaccine, extra: vaccine },
      ghiChu: "Gọi khi tới", tongTamTinh: 10000, trangThai: "CONFIRMED",
    };

    expect(await callOperation("createBooking", payload, true)).toEqual(envelope);

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "post", url: "/bookings" });
    expect(config.headers.getContentType()).not.toContain("application/json");
    expect(config.data).toBeInstanceOf(FormData);
    expect(Array.from(config.data.keys())).toEqual(["payload", "miu-vaccine"]);
    expect(config.data.get("miu-vaccine")).toBe(vaccine);
    expect(JSON.parse(config.data.get("payload"))).toEqual({
      requestId, maKhungGio: 701, ngayTra: "2026-11-23", lienHe: contact,
      thuCung: [
        {
          tenThuCung: "Miu", loaiPet: "MEO", canNangKhaiBao: 5, vaccinationFileKey: "miu-vaccine",
          dichVu: [{ maDichVu: 11, maKhungGio: 704 }],
        },
        {
          tenThuCung: "Mây", loaiPet: "CHO", canNangKhaiBao: 6,
          dichVu: [{ maDichVu: 12, maKhungGio: 701 }],
        },
      ],
      ghiChu: "Gọi khi tới",
    });
  });

  it("maps the legacy booking form to the public endpoint and projects booking aliases for its callers", async () => {
    const vaccine = new File(["vaccination record"], "vaccine.png", { type: "image/png" });
    const payload = {
      requestId, maKhungGio: 701, shiftId: 1, date: "2026-11-23", checkout: "2026-11-25",
      customer: { name: contact.hoTen, phone: "090-123 4567", email: contact.email },
      pets: [
        {
          name: "Miu", species: "MEO", breed: "Mèo ta", coatType: "SHORT", weight: 5,
          type: "SPA", serviceId: 11, comboId: null, roomId: 8, notes: "Dị ứng",
          vaccine, vaccinationFileKey: "miu-vaccine",
        },
        { name: "Mây", species: "CHO", weight: 6, type: "COMBO_SPA_HOTEL", comboId: 12, roomId: 9 },
        { name: "Tít", species: "CHO", weight: 4, type: "HOTEL", serviceId: 11, roomId: 10, vaccine },
      ],
      actor: 1, estimatedPrice: 100000, trangThai: "CONFIRMED",
    };

    const result = await callOperation("createBooking", payload);

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "post", url: "/bookings" });
    expect(JSON.parse(config.data.get("payload"))).toEqual({
      requestId, maKhungGio: 701, ngayTra: "2026-11-25", lienHe: contact,
      thuCung: [
        {
          tenThuCung: "Miu", loaiPet: "MEO", giong: "Mèo ta", loaiLong: "SHORT",
          canNangKhaiBao: 5, ghiChuDiUng: "Dị ứng", dichVu: [{ maDichVu: 11, maKhungGio: 701 }],
          vaccinationFileKey: "miu-vaccine",
        },
        {
          tenThuCung: "Mây", loaiPet: "CHO", canNangKhaiBao: 6, maLoaiPhong: 9,
          dichVu: [{ maCombo: 12, maKhungGio: 701 }],
        },
        {
          tenThuCung: "Tít", loaiPet: "CHO", canNangKhaiBao: 4, maLoaiPhong: 10,
          dichVu: [], vaccinationFileKey: "vaccination-2",
        },
      ],
    });
    expect(config.data.get("miu-vaccine")).toBe(vaccine);
    expect(config.data.get("vaccination-2")).toBe(vaccine);
    expect(result).toMatchObject({
      ...envelope, data: {
        ...booking, id: 101, date: "2026-11-23", tongTienTamTinh: 110000,
        chiTietDonList: [{ id: 15 }],
      },
    });
  });

  it.each([
    ["uses the intake date for Spa-only", { type: "SPA" }, {}, "2026-11-23"],
    ["preserves an explicit ngayTra", { type: "SPA" }, { ngayTra: "2026-11-25" }, "2026-11-25"],
    ["preserves checkout for Hotel", { type: "HOTEL" }, {}, "2026-11-24"],
  ])("%s when the legacy form has different date and checkout values", async (_name, selection, dateOverride, ngayTra) => {
    const payload = {
      requestId, maKhungGio: 701, date: "2026-11-23", checkout: "2026-11-24",
      customer: { name: contact.hoTen, phone: contact.soDienThoai, email: contact.email },
      pets: [{ name: "Miu", species: "MEO", weight: 5, serviceId: 11, roomId: 8, ...selection }],
      ...dateOverride,
    };

    await callOperation("createBooking", payload, true);

    expect(adapter).toHaveBeenCalledTimes(1);
    expect(JSON.parse(adapter.mock.calls[0][0].data.get("payload")).ngayTra).toBe(ngayTra);
  });

  it("rejects a legacy Spa-only booking without an intake date even when checkout is set", async () => {
    const payload = {
      requestId, maKhungGio: 701, checkout: "2026-11-24",
      customer: { name: contact.hoTen, phone: contact.soDienThoai, email: contact.email },
      pets: [{ type: "SPA", name: "Miu", species: "MEO", weight: 5, serviceId: 11, roomId: 8 }],
    };

    await expect(callOperation("createBooking", payload, true)).rejects.toMatchObject({
      success: false, data: null, errorCode: "VALIDATION_FAILED",
      errors: [{ field: "ngayTra", message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  });


  it.each([
    [{ date: "2026-11-23", shiftId: 1 }, "maKhungGio", "MISSING_SLOT_MAPPING"],
    [{
      maKhungGio: 701, lienHe: contact,
      thuCung: [{ dichVu: [{ maDichVu: 11, maKhungGio: 0 }] }],
    }, "thuCung[0].dichVu[0].maKhungGio", "MISSING_SLOT_MAPPING"],
    [{
      maKhungGio: 701, lienHe: contact, thuCung: [{ dichVu: [{ maDichVu: 11, maCombo: 12 }] }],
    }, "thuCung[0].dichVu[0]", "VALIDATION_FAILED"],
    [{
      maKhungGio: 701, lienHe: contact, thuCung: [{ dichVu: [{}] }],
    }, "thuCung[0].dichVu[0]", "VALIDATION_FAILED"],
    [{
      maKhungGio: 701, customer: { phone: "090.123.4567" },
    }, "soDienThoai", "VALIDATION_FAILED"],
    [{
      maKhungGio: 701, lienHe: contact,
      thuCung: [{ vaccinationFileKey: "missing-vaccine", dichVu: [{ maDichVu: 11 }] }],
    }, "thuCung[0].vaccinationFileKey", "VALIDATION_FAILED"],
  ])("rejects invalid booking input for %j before sending HTTP", async (payload, field, errorCode) => {
    await expect(callOperation("createBooking", payload, true)).rejects.toMatchObject({
      success: false, data: null, errorCode, errors: [{ field, message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  });

  it.each([
    ["confirmBooking", { bookingId: 101, id: 999 }, "confirm", {}],
    ["cancelBooking", { bookingId: 101, loaiHuy: "STAFF_CANCELLED", lyDo: "Khách gọi xin hủy" },
      "cancel", { loaiHuy: "STAFF_CANCELLED", lyDo: "Khách gọi xin hủy" }],
    ["reviewBooking", { id: 101, decision: "CONFIRMED" }, "confirm", {}],
    ["reviewBooking", { id: 101, decision: "CANCELLED", reason: "Không đủ chỗ" },
      "cancel", { loaiHuy: "STAFF_REJECTED", lyDo: "Không đủ chỗ" }],
  ])("maps %s with %j to the internal %s command", async (action, input, route, fields) => {
    const randomUUID = vi.spyOn(crypto, "randomUUID");
    const payload = { requestId, ...input, actor: 1, trangThai: "READY", estimatedPrice: 10000 };

    const result = await callOperation(action, payload);

    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "post", url: `/internal/bookings/101/${route}` });
    expect(JSON.parse(config.data)).toEqual({ requestId, ...fields });
    expect(payload.requestId).toBe(requestId);
    expect(randomUUID).not.toHaveBeenCalled();
    if (action === "reviewBooking") {
      expect(result.data).toMatchObject({ ...booking, id: 101 });
    } else {
      expect(result).toEqual(envelope);
    }
  });

  it.each([
    ["reviewBooking", { id: 101, decision: "REJECTED", reason: "Không đủ chỗ" }, "decision"],
    ["reviewBooking", { id: 101, decision: "CANCELLED", reason: "  " }, "lyDo"],
    ["cancelBooking", { bookingId: 101, loaiHuy: "STAFF_CANCELLED" }, "lyDo"],
    ["confirmBooking", { bookingId: 0 }, "bookingId"],
  ])("rejects invalid %s input %j before sending HTTP", async (action, payload, field) => {
    await expect(callOperation(action, payload)).rejects.toMatchObject({
      success: false, data: null, errorCode: "VALIDATION_FAILED",
      errors: [{ field, message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  });

  it("retains the generated request ID on the same payload across a caller-initiated retry", async () => {
    const randomUUID = vi.spyOn(crypto, "randomUUID").mockReturnValue(requestId);
    const payload = {
      maKhungGio: 701, ngayTra: "2026-11-23", lienHe: contact,
      thuCung: [{
        tenThuCung: "Miu", loaiPet: "MEO", canNangKhaiBao: 5, dichVu: [{ maDichVu: 11 }],
      }],
    };
    adapter.mockImplementationOnce(async (config) => {
      throw new AxiosError("Timeout", AxiosError.ECONNABORTED, config);
    });

    await expect(callOperation("createBooking", payload, true)).rejects.toMatchObject({ success: false });
    expect(adapter).toHaveBeenCalledTimes(1);
    expect(payload.requestId).toBe(requestId);

    await expect(callOperation("createBooking", payload, true)).resolves.toEqual(envelope);
    expect(adapter).toHaveBeenCalledTimes(2);
    expect(randomUUID).toHaveBeenCalledTimes(1);
    for (const [config] of adapter.mock.calls) {
      expect(JSON.parse(config.data.get("payload")).requestId).toBe(requestId);
    }
  });
});
