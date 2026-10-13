import { service } from "./operationClient";
export const staffService = service([
  "getStaff",
  "createStaff",
  "toggleStaff",
  "updateAccount",
  "getSettings",
  "updateCapacity",
  "updateShift",
]);
