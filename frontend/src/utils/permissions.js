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
/** Legacy mock menu groups. Target Backend roles are ADMIN/STAFF with resource guards. */
export function canAccess(user, capability) {
  if (!user || user.isActive === false) return false;
  const group =
    user.vaiTro === "ADMIN"
      ? "ADMIN"
      : user.vaiTro === "STAFF"
        ? user.workGroup || "BASE_STAFF"
        : null;
  return !!grants[group]?.includes(capability);
}
export function requireAccess(user, capability) {
  if (!canAccess(user, capability))
    throw new Error("Bạn không có quyền thực hiện thao tác này.");
}
