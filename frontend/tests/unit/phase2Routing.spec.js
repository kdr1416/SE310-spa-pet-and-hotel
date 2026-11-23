// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from "vitest";

let callOperation, http, mockCall;
beforeEach(async () => {
  vi.resetModules();
  vi.stubEnv("VITE_USE_MOCK_API", "false");
  mockCall = vi.fn();
  vi.doMock("../../src/services/mock/mockRepository", () => ({ mockCall }));
  http = (await import("../../src/services/api/httpClient")).default;
  vi.spyOn(http, "get").mockResolvedValue({ success: true, data: [], message: "OK" });
  ({ callOperation } = await import("../../src/services/api/operationClient"));
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllEnvs(); vi.doUnmock("../../src/services/mock/mockRepository"); });

it.each([
  ["getBookingOptions", { ngay: "2035-01-01" }, "/booking-options"],
  ["getAssignmentCandidates", { ngay: "2035-01-01", maCa: 7 }, "/internal/assignment-candidates"],
  ["getFurConditions", {}, "/internal/fur-conditions"],
  ["listCareTasks", { trang: 2, kichThuocTrang: 20, trangThai: "READY" }, "/internal/care-tasks"],
])("routes %s through shared transport without workspace events", async (action, payload, url) => {
  const notify = vi.spyOn(window, "dispatchEvent");
  const response = await callOperation(action, payload, action === "getBookingOptions");
  expect(response).toEqual({ success: true, data: [], message: "OK" });
  expect(http.get).toHaveBeenCalledWith(url, { params: payload });
  expect(mockCall).not.toHaveBeenCalled();
  expect(notify).not.toHaveBeenCalled();
});

it("keeps live errors without falling back to demo and requires an explicit shift", async () => {
  const error = { success: false, errorCode: "FORBIDDEN" };
  http.get.mockRejectedValueOnce(error);
  await expect(callOperation("listCareTasks")).rejects.toBe(error);
  await expect(callOperation("getAssignmentCandidates", { ngay: "2035-01-01" })).rejects.toMatchObject({ errorCode: "VALIDATION_FAILED" });
  expect(mockCall).not.toHaveBeenCalled();
});

it("serves Phase2 demo reads from seed state without HTTP", async () => {
  const { createSeed } = await import("../../src/services/mock/seed");
  const { execute } = await import("../../src/services/mock/mockOperations");
  const seed = createSeed({ demo: true });
  expect(execute(seed, "getBookingOptions", { ngay: "2035-01-01" }).ca).toHaveLength(seed.catalog.shifts.length);
  expect(execute(seed, "getAssignmentCandidates", {}, 1)).toBeInstanceOf(Array);
  expect(execute(seed, "getFurConditions", {}, 1)).toBeInstanceOf(Array);
  expect(execute(seed, "listCareTasks", {}, 1)).toMatchObject({ trang: 1, tongSo: expect.any(Number), items: expect.any(Array) });
  expect(http.get).not.toHaveBeenCalled();
});
