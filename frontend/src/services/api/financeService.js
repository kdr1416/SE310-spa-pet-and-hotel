import { service } from "./operationClient";
export const financeService = service([
  "getFinance",
  "updatePrice",
  "updateSurcharge",
]);
