import { service } from "./operationClient";
export const catalogService = service(["catalog", "getBookingOptions", "getFurConditions"], ["catalog", "getBookingOptions"]);
