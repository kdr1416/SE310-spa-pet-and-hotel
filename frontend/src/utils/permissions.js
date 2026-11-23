const grants = {
  ADMIN: [
    "BOOKING_READ",
    "BOOKING_MANAGE",
    "CARE_UPDATE",
    "SCHEDULE_SELF",
    "FINANCE_MANAGE",
    "STAFF_MANAGE",
    "SYSTEM_MANAGE",
  ],
  RECEPTION: ["BOOKING_READ", "BOOKING_MANAGE", "SCHEDULE_SELF"],
  SPA: ["BOOKING_READ", "CARE_UPDATE", "SCHEDULE_SELF"],
  BASE_STAFF: ["BOOKING_READ", "SCHEDULE_SELF"],
};
// UI visibility capabilities can correspond to several backend permissions.
const liveUiPermissions = new Map([
  ["BOOKING_READ", ["BOOKING_APPROVE"]],
  ["BOOKING_MANAGE", ["BOOKING_APPROVE"]],
  ["CARE_UPDATE", ["CARE_UPDATE"]],
  ["SCHEDULE_SELF", ["SCHEDULE_REGISTER"]],
  ["FINANCE_MANAGE", ["INVOICE_CREATE", "INVOICE_COLLECT"]],
  ["STAFF_MANAGE", ["SCHEDULE_APPROVE", "PERMISSION_MANAGE"]],
  ["SYSTEM_MANAGE", ["SYSTEM_CONFIG"]],
]);

export function canAccess(user, capability) {
  if (!["ADMIN", "STAFF"].includes(user?.vaiTro)) return false;

  // Seed actors retain their workGroup grants even when the API runs live.
  if (user.maNhanVien != null) {
    if ((user.dangHoatDong ?? user.isActive) === false) return false;
    const permissions = Array.isArray(user.quyen) ? user.quyen : [];
    const required = liveUiPermissions.get(capability);
    if (!required) return permissions.includes(capability);
    return (
      user.vaiTro === "ADMIN" ||
      required.some((permission) => permissions.includes(permission))
    );
  }

  if (user.isActive === false) return false;
  const group =
    user.vaiTro === "ADMIN"
      ? "ADMIN"
      : user.workGroup || "BASE_STAFF";
  return !!grants[group]?.includes(capability);
}
export function requireAccess(user, capability) {
  if (!canAccess(user, capability))
    throw new Error("Bạn không có quyền thực hiện thao tác này.");
}
