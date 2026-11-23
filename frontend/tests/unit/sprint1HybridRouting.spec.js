// @vitest-environment jsdom
import { AxiosError } from "axios";
import { beforeEach, describe, expect, it, vi } from "vitest";
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
