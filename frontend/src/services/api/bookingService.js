import { service, callOperation } from "./operationClient";
export const bookingService = service(
  [
    "getWorkspace",
    "getAssignmentCandidates",
    "listCareTasks",
    "checkInPet",
    "performCareAction",
    "addCareLog",
    "handoverPet",
    "getInvoice",
    "checkoutInvoice",
    "createBooking",
    "lookupBooking",
    "reviewBooking",
    "updateBooking",
    "checkIn",
    "careUpdate",
    "createInvoice",
    "addSurcharge",
    "confirmPayment",
    "customerRequest",
    "reviewRequest",
    "resendTracking",
  ],
  ["lookupBooking", "customerRequest", "resendTracking"],
);
export const createCustomerBooking = (payload) =>
  callOperation("createBooking", payload, true);
