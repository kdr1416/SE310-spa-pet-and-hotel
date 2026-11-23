import { IS_MOCK_ENABLED } from "./httpClient";
import { mockCall } from "../mock/mockRepository";
import { getStoredLiveRoleForDemoActor } from "../../stores/auth";
import {
  callSprint1Operation,
  isSprint1Mutation,
  supportsSprint1Action,
} from "./sprint1Adapter";

const unsupportedDemoActions = new Set([
  "updateBooking",
  "addSurcharge",
  "customerRequest",
  "reviewRequest",
  "resendTracking",
  "updateAccount",
]);

function notifyWorkspaceUpdated() {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new Event("petspa-workspace-updated"));
  try {
    localStorage.setItem("petspa_workspace_update", `${Date.now()}:${Math.random()}`);
  } catch {
    // Storage may be disabled.
  }
}

export async function callOperation(action, payload = {}, isPublic = false) {
  if (!IS_MOCK_ENABLED && supportsSprint1Action(action)) {
    const result = await callSprint1Operation(action, payload);
    if (!isPublic && result.success === true && isSprint1Mutation(action)) {
      notifyWorkspaceUpdated();
    }
    return result;
  }

  // These actions can reference live data through IDs also present in the demo database.
  if (!IS_MOCK_ENABLED && unsupportedDemoActions.has(action)) {
    throw {
      success: false,
      data: null,
      message: "Thao tác này chưa hỗ trợ dữ liệu từ Backend.",
      errorCode: "HYBRID_ENTITY_UNSUPPORTED",
      errors: [],
    };
  }

  let actor = null;
  let expectedRole;
  if (!isPublic) {
    try {
      actor = JSON.parse(
        sessionStorage.getItem("petspa_mock_session") || "null",
      )?.id;
      if (!IS_MOCK_ENABLED) expectedRole = getStoredLiveRoleForDemoActor(actor);
    } catch {
      actor = null;
    }
    if (!actor || (!IS_MOCK_ENABLED && !expectedRole)) {
      if (!IS_MOCK_ENABLED) sessionStorage.removeItem("petspa_mock_session");
      throw new Error("Vui lòng đăng nhập lại.");
    }
  }
  const data = expectedRole
    ? await mockCall(action, payload, actor, { expectedRole })
    : await mockCall(action, payload, actor);
  if (!isPublic && !action.startsWith("get")) {
    notifyWorkspaceUpdated();
  }
  return { success: true, data, message: "Đã lưu dữ liệu mock.", errors: [] };
}

export const service = (actions, publicActions = []) =>
  Object.fromEntries(
    actions.map((action) => [
      action,
      (payload) =>
        callOperation(action, payload, publicActions.includes(action)),
    ]),
  );
