export function hasStaffPermission(zev: { isStaff?: boolean; permissions?: string[] } | undefined,
  key: string): boolean {
  return Boolean(zev?.isStaff && zev.permissions?.includes(key));
}

export function canShowPaymentAction(permissions: string[] | undefined, key: string): boolean {
  return !permissions || permissions.includes(key);
}
