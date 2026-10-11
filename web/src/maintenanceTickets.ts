// Fault reports: where the resident reports (own unit / common parts) and the "new report" bell.
export type TicketPlace = "unit" | "common";

export const ticketPlaces: { value: TicketPlace; label: string }[] = [
  { value: "unit", label: "Moj stan" }, { value: "common", label: "Zajednički prostori" }];

// The API takes the unit for "Moj stan" and no unit for common parts.
export function ticketUnitId(place: TicketPlace, unitId: string): string | null {
  return place === "unit" && unitId ? unitId : null;
}

export function canSubmitTicket(place: TicketPlace, unitId: string): boolean {
  return place === "common" || Boolean(unitId);
}

// 1 nova prijava, 2–4 nove prijave, 5+ novih prijava (11–14 always "novih").
export function unreadLabel(count: number): string {
  if (count <= 0) return "Nema novih prijava kvarova";
  const lastTwo = count % 100;
  const last = count % 10;
  if (last === 1 && lastTwo !== 11) return `${count} nova prijava kvara`;
  if (last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14)) return `${count} nove prijave kvara`;
  return `${count} novih prijava kvara`;
}

export function bellBadge(count: number): string | null {
  return count <= 0 ? null : count > 9 ? "9+" : String(count);
}

// Who sees the bell and "Novo" / "Označi pročitano": whoever handles maintenance in the active ZEV.
export function handlesTickets(permissions: string[] | undefined, isSystemAdministrator = false): boolean {
  return isSystemAdministrator || Boolean(permissions?.includes("Maintenance.Edit") || permissions?.includes("Maintenance.Approve"));
}
