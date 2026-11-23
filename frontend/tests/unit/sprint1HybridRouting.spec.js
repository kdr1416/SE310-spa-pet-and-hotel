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

describe("live care and billing commands", () => {
  const requestId = "550e8400-e29b-41d4-a716-446655440000";
  const careLog = {
    maNhatKy: 73, loaiNhatKy: "NOTE", ghiChu: "Đã ăn", anhUrl: null, canNangTheoDoi: 5,
    tenTacGia: "Nhân viên thử nghiệm", ghiNhanAt: "2026-11-23T04:00:00Z",
  };
  const pet = {
    maChiTietDon: 15, maThuCung: 25, tenThuCung: "Miu", trangThai: "READY",
    canNangChot: 5, sizeThucTe: "S", sizeChot: "S", tinhTrangLong: ["BINH_THUONG"],
    maLoaiPhong: null, tenNhanVienPhuTrach: "Nhân viên thử nghiệm", dichVu: [],
    nhatKy: [careLog], nhanSom: false, banGiaoAt: null, traLaiAt: null,
  };
  const invoice = {
    maHoaDon: 51, soHoaDon: "HD-51", maDon: 101, thayTheHoaDon: null, trangThai: "UNPAID",
    lapAt: "2026-11-23T04:00:00Z", nguoiLap: "Nhân viên thử nghiệm",
    khach: { hoTen: "Khách thử nghiệm", soDienThoai: "0901234567" },
    ngayHen: "2026-11-23", dongTien: [{
      maDongHoaDon: 8, loaiKhoan: "SPA", maChiTietDon: 15, maDichVuTrongDon: 35, maPhuPhi: null,
      tenPet: "Miu", tenKhoan: "Tắm", size: "S", soLuong: 1,
      donGia: 120000, thanhTien: 120000, dienGiai: null,
    }],
    tongTien: 120000, cocKhauTru: 20000, conPhaiTra: 100000, paidAt: null, thuHoiAt: null, qr: null,
  };
  const paidInvoice = { ...invoice, trangThai: "PAID", paidAt: "2026-11-23T04:30:00Z" };
  let callOperation;
  let liveHttpClient;
  let originalAdapter;
  let adapter;

  function respondWith(data, status = 200) {
    const envelope = { success: true, data, message: "Thành công", traceId: "test-command-trace" };
    adapter.mockImplementation(async (config) => ({
      config, data: envelope, status, statusText: "", headers: {},
    }));
    return envelope;
  }

  function expectRequest(url) {
    expect(adapter).toHaveBeenCalledTimes(1);
    const [config] = adapter.mock.calls[0];
    expect(config).toMatchObject({ method: "post", url });
    return config;
  }

  function expectMultipart(url, dto, attachment) {
    const config = expectRequest(url);
    expect(config.headers.getContentType()).not.toContain("application/json");
    expect(config.data).toBeInstanceOf(FormData);
    expect([...config.data.keys()]).toEqual(attachment ? ["payload", attachment[0]] : ["payload"]);
    expect(JSON.parse(config.data.get("payload"))).toEqual(dto);
    if (attachment) expect(config.data.get(attachment[0])).toBe(attachment[1]);
  }

  async function expectLocalRejection(action, payload, field) {
    await expect(callOperation(action, payload)).rejects.toMatchObject({
      success: false, data: null, errorCode: "VALIDATION_FAILED",
      errors: [{ field, message: expect.any(String) }],
    });
    expect(adapter).not.toHaveBeenCalled();
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    vi.stubEnv("VITE_USE_MOCK_API", "false");
    vi.resetModules();
    ({ default: liveHttpClient } = await import("../../src/services/api/httpClient"));
    ({ callOperation } = await import("../../src/services/api/operationClient"));
    originalAdapter = liveHttpClient.defaults.adapter;
    adapter = vi.fn();
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

  describe("commands-care", () => {
    const intakePhoto = new File(["intake fixture"], "intake.png", { type: "image/png" });
    const finishPhoto = new File(["finish fixture"], "finish.png", { type: "image/png" });
    const checkin = {
      canNangThucTe: 5, tinhTrangLong: ["BINH_THUONG", "LONG_NGAN"], maNhanVien: 7,
      ghiChuSucKhoe: "Không thấy vết thương", quyetDinhGia: "THU_GON", xacNhanKhach: true,
      noiDungThuGon: "Chỉ tắm", maHoSoTiemChung: 8, xacNhanGiayHopLe: true,
    };

    it("posts canonical checkInPet with the original photo separated from Checkin JSON", async () => {
      const envelope = respondWith({ ...pet, trangThai: "WAITING" });
      const result = await callOperation("checkInPet", {
        ...checkin, requestId, maChiTietDon: 15, detailId: 99, id: 101, anhTiepNhan: intakePhoto,
        weight: 99, nhanSom: true,
      });

      expectMultipart("/internal/booking-pets/15/check-in", { requestId, ...checkin },
        ["anhTiepNhan", intakePhoto]);
      expect(result).toEqual(envelope);
    });

    it("maps checkIn aliases using the pet detail ID and keeps the supplied fur code", async () => {
      const envelope = respondWith({ ...pet, trangThai: "WAITING" });
      const result = await callOperation("checkIn", {
        requestId, id: 101, detailId: 15, weight: 5, coat: "BINH_THUONG", employeeId: 7,
        ghiChuSucKhoe: "Không thấy vết thương", quyetDinhGia: "SIZE_THUC_TE",
        xacNhanKhach: true, anhTiepNhan: intakePhoto,
      });

      expectMultipart("/internal/booking-pets/15/check-in", {
        requestId, canNangThucTe: 5, tinhTrangLong: ["BINH_THUONG"], maNhanVien: 7,
        ghiChuSucKhoe: "Không thấy vết thương", quyetDinhGia: "SIZE_THUC_TE", xacNhanKhach: true,
      }, ["anhTiepNhan", intakePhoto]);
      expect(result).toMatchObject({
        ...envelope, data: { ...envelope.data, id: 15, checkedIn: true, trangThaiChamSoc: "WAITING" },
      });
    });

    it.each([
      [{ anhTiepNhan: undefined }, "anhTiepNhan"],
      [{ ghiChuSucKhoe: " " }, "ghiChuSucKhoe"],
      [{ maNhanVien: undefined }, "maNhanVien"],
      [{ quyetDinhGia: undefined }, "quyetDinhGia"],
      [{ canNangThucTe: undefined }, "canNangThucTe"],
      [{ xacNhanKhach: "true" }, "xacNhanKhach"],
      [{ noiDungThuGon: undefined }, "noiDungThuGon"],
      [{ id: 101, detailId: undefined }, "maChiTietDon"],
      [{ tinhTrangLong: undefined, coat: "Bình thường" }, "tinhTrangLong"],
    ])("rejects incomplete check-in input %j before HTTP", async (override, field) => {
      await expectLocalRejection(
        "checkIn", { ...checkin, detailId: 15, anhTiepNhan: intakePhoto, ...override }, field,
      );
    });

    it("keeps the explicit action and photo for canonical READY", async () => {
      const envelope = respondWith(pet);

      const result = await callOperation("performCareAction", {
        requestId, id: 15, hanhDong: "READY", next: "PAUSED", ghiChu: "Đã hoàn tất",
        anhThanhPham: finishPhoto, weight: 5,
      });

      expectMultipart("/internal/booking-pets/15/care-actions",
        { requestId, hanhDong: "READY", ghiChu: "Đã hoàn tất" }, ["anhThanhPham", finishPhoto]);
      expect(result).toEqual(envelope);
    });

    it.each([
      ["WAITING", "IN_PROGRESS", "START", undefined, undefined],
      ["PAUSED", "IN_PROGRESS", "RESUME", undefined, undefined],
      ["IN_PROGRESS", "PAUSED", "PAUSE", "Chờ thú cưng bình tĩnh", undefined],
      ["IN_PROGRESS", "READY", "READY", "", finishPhoto],
    ])("maps careUpdate from %s to %s as %s", async (currentState, next, hanhDong, note, photo) => {
      const envelope = respondWith({ ...pet, trangThai: next });
      const result = await callOperation("careUpdate", {
        requestId, id: 101, detailId: 15, currentState, next, note, photo, weight: 5,
      });

      const dto = { requestId, hanhDong };
      if (note) dto.ghiChu = note;
      expectMultipart("/internal/booking-pets/15/care-actions", dto,
        photo ? ["anhThanhPham", photo] : undefined);
      expect(result).toMatchObject({ ...envelope, data: { ...envelope.data, id: 15, trangThaiChamSoc: next } });
    });

    it("sends an explicit READY photo exception with no fabricated photo", async () => {
      respondWith(pet);

      await callOperation("performCareAction", {
        requestId, maChiTietDon: 15, hanhDong: "READY", maNgoaiLeAnh: 13,
      });

      expectMultipart("/internal/booking-pets/15/care-actions",
        { requestId, hanhDong: "READY", maNgoaiLeAnh: 13 });
    });

    it.each([
      ["careUpdate", { next: "IN_PROGRESS" }, "currentState"],
      ["careUpdate", { next: "DONE" }, "hanhDong"],
      ["careUpdate", { id: 101, detailId: undefined, next: "PAUSED", note: "Chờ" }, "maChiTietDon"],
      ["performCareAction", { hanhDong: "PAUSE", ghiChu: " " }, "ghiChu"],
      ["performCareAction", { hanhDong: "READY" }, "anhThanhPham"],
      ["addCareLog", { canNangTheoDoi: 5 }, "ghiChu"],
      ["handoverPet", { xacNhanDaNhan: false }, "xacNhanDaNhan"],
    ])("rejects %s when its required input is missing from %j", async (action, input, field) => {
      await expectLocalRejection(action, { detailId: 15, ...input }, field);
    });

    it.each([
      ["notes and weight", { ghiChu: "Đã ăn", canNangTheoDoi: 5 },
        { ghiChu: "Đã ăn", canNangTheoDoi: 5 }, undefined],
      ["a photo", { anh: finishPhoto }, {}, ["anh", finishPhoto]],
    ])("posts a separate care log with %s and its own request ID", async (_name, input, dto, attachment) => {
      const logRequestId = "550e8400-e29b-41d4-a716-446655440001";
      const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValueOnce(requestId).mockReturnValueOnce(logRequestId);
      respondWith({ ...pet, trangThai: "IN_PROGRESS" });
      await callOperation("performCareAction", { id: 15, hanhDong: "START" });
      expectRequest("/internal/booking-pets/15/care-actions");
      adapter.mockClear();

      const envelope = respondWith(careLog, 201);
      const payload = { detailId: 15, id: 101, ...input };
      const result = await callOperation("addCareLog", payload);

      expectMultipart("/internal/booking-pets/15/care-logs", { requestId: logRequestId, ...dto }, attachment);
      expect(payload.requestId).toBe(logRequestId);
      expect(uuid).toHaveBeenCalledTimes(2);
      expect(result).toEqual(envelope);
    });

    it("posts a standalone handover command only with explicit confirmation", async () => {
      const envelope = respondWith({ ...pet, trangThai: "DONE", banGiaoAt: "2026-11-23T05:00:00Z" });

      const result = await callOperation("handoverPet", {
        requestId, maChiTietDon: 15, id: 101, xacNhanDaNhan: true, trangThai: "READY",
      });

      const config = expectRequest("/internal/booking-pets/15/handover");
      expect(JSON.parse(config.data)).toEqual({ requestId, xacNhanDaNhan: true });
      expect(result).toEqual(envelope);
    });
  });

  describe("commands-billing", () => {
    const collect = { phuongThuc: "CASH", soTienThucNhan: 120000, soTienDaTraLai: 20000, bangChungTraThua: "Đã trả tại quầy" };

    it.each([{ id: 101 }, { bookingId: 101, id: 99 }])(
      "creates a single invoice with %j without collecting payment", async (identifiers) => {
        const envelope = respondWith(invoice, 201);

        const result = await callOperation("createInvoice", { ...identifiers, requestId, ...collect });

        const config = expectRequest("/internal/bookings/101/invoices");
        expect(JSON.parse(config.data)).toEqual({ requestId });
        expect(result).toEqual(envelope);
      },
    );

    it.each([
      ["checkoutInvoice", { id: 51 }, collect, collect],
      ["checkoutInvoice", { invoiceId: 51, maHoaDon: 99, id: 101 },
        { phuongThuc: "BANK_TRANSFER", soTienThucNhan: 100000, soTienDaTraLai: 0, thamChieu: "TT-51" },
        { phuongThuc: "BANK_TRANSFER", soTienThucNhan: 100000, soTienDaTraLai: 0, thamChieu: "TT-51" }],
      ["confirmPayment", { id: 101, invoiceId: 51 },
        { method: "CASH", soTienThucNhan: 120000, soTienDaTraLai: 20000, bangChungTraThua: "Đã trả tại quầy" }, collect],
      ["confirmPayment", { id: 101, maHoaDon: 51 }, collect, collect],
      ["confirmPayment", { id: 101, hoaDon: invoice }, collect, collect],
    ])("collects a real invoice once via %s with %j", async (action, identifiers, input, dto) => {
      const envelope = respondWith(paidInvoice);
      const bookingPet = { id: 15, trangThai: "READY", trangThaiChamSoc: "READY" };

      const result = await callOperation(action, {
        ...identifiers, requestId, ...input, conPhaiTra: 100000, chiTietDonList: [bookingPet],
      });

      const config = expectRequest("/internal/invoices/51/checkout");
      expect(JSON.parse(config.data)).toEqual({ requestId, ...dto });
      expect(bookingPet).toEqual({ id: 15, trangThai: "READY", trangThaiChamSoc: "READY" });
      if (action === "checkoutInvoice") {
        expect(result).toEqual(envelope);
      } else {
        expect(result).toMatchObject({
          ...envelope, data: { ...paidInvoice, id: 51, trangThaiThanhToan: "PAID" },
        });
      }
    });

    it.each([
      [{ id: 101, method: "CASH" }, "invoiceId"],
      [{ id: 101, ...collect }, "invoiceId"],
      [{ invoiceId: 51, ...collect, phuongThuc: undefined }, "phuongThuc"],
      [{ invoiceId: 51, ...collect, soTienThucNhan: undefined, conPhaiTra: 100000 }, "soTienThucNhan"],
      [{ invoiceId: 51, ...collect, soTienDaTraLai: undefined }, "soTienDaTraLai"],
    ])("rejects confirmPayment input %j before creating or collecting an invoice", async (input, field) => {
      await expectLocalRejection("confirmPayment", input, field);
    });

    it("preserves a checkout conflict and waits for the caller to retry with the same request ID", async () => {
      const uuid = vi.spyOn(crypto, "randomUUID").mockReturnValue(requestId);
      const failure = {
        success: false, data: null, message: "Số tiền không khớp", errorCode: "AMOUNT_MISMATCH",
        errors: [], traceId: "test-checkout-trace",
      };
      const payload = { invoiceId: 51, ...collect };
      const envelope = respondWith(paidInvoice);
      adapter.mockImplementationOnce(async (config) => {
        throw new AxiosError(failure.message, AxiosError.ERR_BAD_REQUEST, config, {}, {
          config, status: 409, data: failure,
        });
      });

      await expect(callOperation("checkoutInvoice", payload)).rejects.toEqual(failure);
      expectRequest("/internal/invoices/51/checkout");
      expect(payload.requestId).toBe(requestId);

      await expect(callOperation("checkoutInvoice", payload)).resolves.toEqual(envelope);
      expect(adapter).toHaveBeenCalledTimes(2);
      expect(uuid).toHaveBeenCalledTimes(1);
      for (const [config] of adapter.mock.calls) expect(JSON.parse(config.data).requestId).toBe(requestId);
    });
  });
});

describe("hybrid-policy", () => {
  const requestId = "550e8400-e29b-41d4-a716-446655440006";
  const bookingId = 101;
  const trackingPair = { trackingCode: "ABCD1234", phone: "0900000000" };
  const liveFailure = {
    success: false,
    data: null,
    message: "Cần quyền INVOICE_CREATE",
    errorCode: "FORBIDDEN",
    errors: [],
    traceId: "test-hybrid-live-trace",
  };

  let callOperation, mockCall, seed, adapter, updated, setItem;

  function setDemoActor(id) {
    sessionStorage.setItem("petspa_mock_session", JSON.stringify({ id }));
  }

  function respondWith(data, envelope = { success: true, data, message: "Thành công" }) {
    adapter.mockImplementation(async (config) => ({
      config, data: envelope, status: 200, statusText: "", headers: {},
    }));
    return envelope;
  }

  function expectWorkspaceSignals(count) {
    const storageUpdates = setItem.mock.calls.filter(([key]) => key === "petspa_workspace_update");
    expect(updated).toHaveBeenCalledTimes(count);
    expect(storageUpdates).toHaveLength(count);
    if (count > 0) {
      expect(localStorage.getItem("petspa_workspace_update")).toBe(storageUpdates.at(-1)[1]);
    }
  }

  async function startClient(mode = "false") {
    vi.stubEnv("VITE_USE_MOCK_API", mode ?? undefined);
    const { default: client } = await import("../../src/services/api/httpClient");
    ({ callOperation } = await import("../../src/services/api/operationClient"));
    adapter = vi.fn(async () => { throw new Error("Unexpected HTTP request"); });
    client.defaults.adapter = adapter;
  }

  beforeEach(async () => {
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();

    const { createSeed } = await import("../../src/services/mock/seed");
    const { execute } = await import("../../src/services/mock/mockOperations");
    seed = createSeed({ demo: true });
    seed.bookings[0].id = bookingId;
    seed.bookings[0].trackingCode = trackingPair.trackingCode;
    // Keep the real demo operations and replace only their IndexedDB boundary.
    mockCall = vi.fn(async (action, payload, actor) => execute(seed, action, payload, actor));
    vi.doMock("../../src/services/mock/mockRepository", () => ({ mockCall }));

    updated = vi.fn();
    window.addEventListener("petspa-workspace-updated", updated);
    setItem = vi.spyOn(Storage.prototype, "setItem");
  });

  afterEach(() => {
    window.removeEventListener("petspa-workspace-updated", updated);
    vi.restoreAllMocks();
    vi.doUnmock("../../src/services/mock/mockRepository");
    vi.unstubAllEnvs();
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();
  });

  it.each([["default", null], ["enabled", "true"]])(
    "preserves demo reads, writes and public lookup in %s mock mode", async (_name, mode) => {
      await startClient(mode);
      setDemoActor(3);

      await expect(callOperation("getWorkspace")).resolves.toMatchObject({
        success: true, data: { bookings: [expect.objectContaining({ id: bookingId }), expect.any(Object)] },
      });
      expect(mockCall).toHaveBeenLastCalledWith("getWorkspace", {}, 3);
      expectWorkspaceSignals(0);

      setDemoActor(1);
      const change = { id: bookingId, note: "Demo booking note" };
      await expect(callOperation("updateBooking", change)).resolves.toMatchObject({
        success: true, data: { ghiChuKhach: change.note },
      });
      expect(mockCall).toHaveBeenLastCalledWith("updateBooking", change, 1);

      sessionStorage.clear();
      await expect(callOperation("lookupBooking", trackingPair, true)).resolves.toMatchObject({
        success: true, data: { id: bookingId, ghiChuKhach: change.note },
      });
      expect(mockCall).toHaveBeenLastCalledWith("lookupBooking", trackingPair, null);
      expect(adapter).not.toHaveBeenCalled();
      expectWorkspaceSignals(1);
    },
  );

  it.each([["getSettings", 1, "vaccinePolicy"], ["getFinance", 1, "ledger"], ["getSchedule", 2, "users"]])(
    "uses the stored demo actor for %s", async (action, demoId, resultKey) => {
      await startClient();
      setDemoActor(demoId);

      const result = await callOperation(action);

      expect(result.success).toBe(true);
      expect(result.data).toHaveProperty(resultKey);
      expect(mockCall).toHaveBeenCalledExactlyOnceWith(action, {}, demoId);
      expect(adapter).not.toHaveBeenCalled();
      expectWorkspaceSignals(0);
    },
  );

  it("uses demo Staff 2 for schedule after live Staff 1 logs in", async () => {
    await startClient();
    const { useAuthStore } = await import("../../src/stores/auth");
    const auth = useAuthStore(createPinia());
    const staff = {
      maNhanVien: 1,
      tenNhanVien: "Nhân viên live",
      vaiTro: "STAFF",
      quyen: ["SCHEDULE_REGISTER"],
      dangHoatDong: true,
    };
    respondWith({ accessToken: "test-staff-token", nhanVien: staff });
    expect((await auth.login("test-staff", "test-password")).success).toBe(true);
    const identity = auth.user;
    const storedIdentity = localStorage.getItem("petspa_auth_user");

    const result = await callOperation("getSchedule");

    expect(result.data.users).toMatchObject([{ id: 2, vaiTro: "STAFF" }]);
    expect(mockCall).toHaveBeenCalledExactlyOnceWith("getSchedule", {}, 2);
    expect(adapter).toHaveBeenCalledTimes(1);
    expect(auth.user).toBe(identity);
    expect(auth.user).toMatchObject({ ...staff, id: 1, role: "STAFF" });
    expect(auth.isAdmin).toBe(false);
    expect(localStorage.getItem("petspa_auth_user")).toBe(storedIdentity);
    expectWorkspaceSignals(0);
  });

  it.each([null, "{", '{"id":3}', '{"id":"2"}'])(
    "rejects a private demo fallback with invalid stored session %j", async (session) => {
      await startClient();
      if (session !== null) sessionStorage.setItem("petspa_mock_session", session);

      await expect(callOperation("getSchedule")).rejects.toMatchObject({
        message: "Vui lòng đăng nhập lại.",
      });

      expect(mockCall).not.toHaveBeenCalled();
      expect(adapter).not.toHaveBeenCalled();
      expectWorkspaceSignals(0);
    },
  );

  it("lets demo operations reject an unknown action without sending HTTP", async () => {
    await startClient();
    setDemoActor(1);

    await expect(callOperation("unknownOperation")).rejects.toThrow("Thao tác chưa được hỗ trợ.");

    expect(mockCall).toHaveBeenCalledExactlyOnceWith("unknownOperation", {}, 1);
    expect(adapter).not.toHaveBeenCalled();
    expectWorkspaceSignals(0);
  });

  it.each([
    ["updateBooking", { id: bookingId, bookingId, note: "Live booking note" }, false],
    ["addSurcharge", { id: bookingId, bookingId, ruleId: 1, note: "Live surcharge" }, false],
    ["customerRequest", { ...trackingPair, type: "ADDON", note: "Live request" }, true],
    ["reviewRequest", { id: bookingId, approved: true }, false],
    ["resendTracking", { ...trackingPair, email: "demo@example.test" }, true],
    ["updateAccount", { id: 1, name: "Live account" }, false],
  ])("blocks %s before it can reach demo data", async (action, payload, isPublic) => {
    await startClient();
    if (!isPublic) setDemoActor(1);

    await expect(callOperation(action, payload, isPublic)).rejects.toMatchObject({
      success: false, data: null, errorCode: "HYBRID_ENTITY_UNSUPPORTED",
    });

    expect(mockCall).not.toHaveBeenCalled();
    expect(adapter).not.toHaveBeenCalled();
    expectWorkspaceSignals(0);
  });

  it("publishes one workspace event and storage signal after a private live mutation without a demo session", async () => {
    await startClient();
    localStorage.setItem("petspa_auth_token", "test-admin-token");
    const envelope = respondWith({ maHoaDon: 23, trangThai: "UNPAID" });

    await expect(callOperation("createInvoice", { bookingId, requestId })).resolves.toEqual(envelope);

    expect(adapter).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({
      method: "post", url: "/internal/bookings/101/invoices",
    }));
    expect(mockCall).not.toHaveBeenCalled();
    expectWorkspaceSignals(1);
  });

  it.each([
    ["checkAvailability", { maKhungGio: 73 }, "/availability"],
    ["lookupTracking", { trackingCode: "ABCD1234", soDienThoai: "0900000000" }, "/tracking/lookup"],
  ])("keeps %s quiet even when invoked without the public flag", async (action, payload, url) => {
    await startClient();
    const envelope = respondWith({});

    await expect(callOperation(action, payload)).resolves.toEqual(envelope);

    expect(adapter).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ method: "post", url }));
    expect(mockCall).not.toHaveBeenCalled();
    expectWorkspaceSignals(0);
  });

  it("keeps a successful public booking quiet", async () => {
    await startClient();
    const { createCustomerBooking } = await import("../../src/services/api/bookingService");
    const envelope = respondWith({ maDon: bookingId, trackingCode: "ABCD1234" });

    await expect(createCustomerBooking({
      requestId,
      maKhungGio: 73,
      lienHe: { hoTen: "Khách test", soDienThoai: "0900000000", email: "customer@example.test" },
      thuCung: [{ tenThuCung: "Miu", loaiPet: "MEO", canNangKhaiBao: 5, dichVu: [{ maDichVu: 11 }] }],
    })).resolves.toEqual(envelope);

    expect(adapter).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ method: "post", url: "/bookings" }));
    expect(mockCall).not.toHaveBeenCalled();
    expectWorkspaceSignals(0);
  });

  it.each(["http", "network", "validation", "unsuccessful envelope"])(
    "keeps %s failure free of demo fallback and workspace signals", async (failureKind) => {
      await startClient();
      setDemoActor(1);
      if (failureKind === "http") {
        adapter.mockImplementation(async (config) => {
          throw new AxiosError(liveFailure.message, AxiosError.ERR_BAD_REQUEST, config, {}, {
            config, status: 403, data: liveFailure,
          });
        });
      }
      if (failureKind === "network") {
        adapter.mockImplementation(async (config) => {
          throw new AxiosError("Network Error", AxiosError.ERR_NETWORK, config, {});
        });
      }
      if (failureKind === "unsuccessful envelope") respondWith(null, liveFailure);

      const operation = callOperation("createInvoice", {
        bookingId: failureKind === "validation" ? undefined : bookingId, requestId,
      });

      if (failureKind === "unsuccessful envelope") await expect(operation).resolves.toEqual(liveFailure);
      else if (failureKind === "http") await expect(operation).rejects.toEqual(liveFailure);
      else await expect(operation).rejects.toMatchObject({ success: false, data: null });

      expect(adapter).toHaveBeenCalledTimes(failureKind === "validation" ? 0 : 1);
      expect(mockCall).not.toHaveBeenCalled();
      expectWorkspaceSignals(0);
    },
  );

  it("signals completion of a demo settings mutation without sending HTTP", async () => {
    await startClient();
    setDemoActor(1);
    const policy = { SPA: false, HOTEL: true, COMBO_SPA_HOTEL: true };

    await expect(callOperation("updateVaccinePolicy", { policy })).resolves.toMatchObject({
      success: true, data: policy,
    });

    expect(seed.settings.vaccinePolicy).toEqual(policy);
    expect(mockCall).toHaveBeenCalledExactlyOnceWith("updateVaccinePolicy", { policy }, 1);
    expect(adapter).not.toHaveBeenCalled();
    expectWorkspaceSignals(1);
  });
});

describe("notifications", () => {
  let wrapper, getWorkspace;

  function dispatchWorkspaceUpdate() {
    window.dispatchEvent(new Event("petspa-workspace-updated"));
  }

  function dispatchWorkspaceStorageUpdate() {
    window.dispatchEvent(new StorageEvent("storage", { key: "petspa_workspace_update" }));
  }

  beforeEach(async () => {
    vi.resetModules();
    vi.stubEnv("VITE_USE_MOCK_API", "false");
    localStorage.clear();
    vi.spyOn(document, "hidden", "get").mockReturnValue(false);
    vi.doMock("../../src/stores/auth", () => ({
      useAuthStore: () => ({
        user: { vaiTro: "STAFF" },
        isAuthenticated: true,
        isAdmin: false,
        initAuth: async () => {},
        can: (permission) => permission === "BOOKING_MANAGE",
      }),
    }));
    vi.doMock("vue-router", () => ({ useRouter: () => ({ replace: vi.fn() }) }));

    const { default: client } = await import("../../src/services/api/httpClient");
    const { bookingService } = await import("../../src/services/api/bookingService");
    const { default: BookingNotifications } = await import("../../src/components/shared/BookingNotifications.vue");
    const { mount, flushPromises } = await import("@vue/test-utils");
    client.defaults.adapter = async (config) => ({
      config,
      status: 200,
      statusText: "OK",
      headers: {},
      data: {
        success: true,
        data: { items: [], trang: 1, kichThuocTrang: 100, tongSo: 0 },
        message: "Thành công",
      },
    });
    getWorkspace = vi.spyOn(bookingService, "getWorkspace");

    vi.useFakeTimers();
    wrapper = mount(BookingNotifications, { global: { stubs: { RouterLink: true } } });
    await flushPromises();
    expect(getWorkspace).toHaveBeenCalledTimes(1);
    getWorkspace.mockClear();
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.doUnmock("vue-router");
    vi.doUnmock("../../src/stores/auth");
    vi.unstubAllEnvs();
    vi.resetModules();
    localStorage.clear();
  });

  it("waits 300 ms from the last workspace or storage event and refreshes once", async () => {
    dispatchWorkspaceUpdate();
    await vi.advanceTimersByTimeAsync(50);
    dispatchWorkspaceUpdate();
    await vi.advanceTimersByTimeAsync(50);
    dispatchWorkspaceUpdate();
    await vi.advanceTimersByTimeAsync(50);
    dispatchWorkspaceStorageUpdate();

    await vi.advanceTimersByTimeAsync(299);
    expect(getWorkspace).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    expect(getWorkspace).toHaveBeenCalledTimes(1);
  });

  it("cancels queued refreshes and ignores subsequent events after unmount", async () => {
    dispatchWorkspaceStorageUpdate();
    await vi.advanceTimersByTimeAsync(100);
    wrapper.unmount();
    wrapper = undefined;

    dispatchWorkspaceUpdate();
    dispatchWorkspaceStorageUpdate();
    await vi.advanceTimersByTimeAsync(300);

    expect(getWorkspace).not.toHaveBeenCalled();
  });

  it("does not schedule another refresh after a workspace read succeeds", async () => {
    dispatchWorkspaceUpdate();
    await vi.advanceTimersByTimeAsync(300);
    expect(getWorkspace).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(600);
    expect(getWorkspace).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem("petspa_workspace_update")).toBeNull();
  });
});
