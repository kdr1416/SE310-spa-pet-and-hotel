import { IS_MOCK_ENABLED } from "./httpClient";
import { mockCall } from "../mock/mockRepository";
export async function callOperation(action, payload = {}, isPublic = false) {
  if (!IS_MOCK_ENABLED)
    throw new Error(
      "Chức năng này cần hợp đồng API Backend; hiện chỉ có mock.",
    );
  let actor = null;
  if (!isPublic) {
    try {
      actor = JSON.parse(
        sessionStorage.getItem("petspa_mock_session") || "null",
      )?.id;
    } catch {
      actor = null;
    }
    if (!actor) throw new Error("Vui lòng đăng nhập lại.");
  }
  const data = await mockCall(action, payload, actor);
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
