import { service } from "./operationClient";
export const scheduleService = service([
  "getSchedule",
  "createPeriod",
  "togglePeriod",
  "registerShift",
  "reviewShift",
  "publishSchedule",
  "updateClosedDays",
]);
