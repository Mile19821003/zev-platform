// Maintenance calendar view rules. The API derives the status: an open occurrence is Planned before its date and
// Due from its date on; InProgress / Completed / Cancelled come from the work done.
export const scheduleStatusLabels: Record<string, string> = {
  Planned: "Planirano", Due: "Dospjelo", InProgress: "U toku", Completed: "Završeno", Cancelled: "Otkazano",
};

export function scheduleStatusLabel(status: string): string {
  return scheduleStatusLabels[status] ?? status;
}

export function scheduleBadgeClass(status: string): string {
  return `badge schedule-${status.toLowerCase()}`;
}

// "Završeno" is offered once the work is due or under way; future occurrences wait for their date.
export function canCompleteSchedule(status: string): boolean {
  return status === "Due" || status === "InProgress";
}

// Periodic plans: status and interval as shown in the plans table.
export function planStatusLabel(isActive: boolean): string {
  return isActive ? "Aktivan" : "Deaktiviran";
}

export function planFrequencyText(days: number): string {
  if (days === 1) return "svaki dan";
  if (days === 7) return "svake sedmice";
  return `svakih ${days} dana`;
}

export const planDeleteWarning = "Plan i njegovi neizvršeni termini biće obrisani. Sistemski administrator ga može vratiti iz „Obrisani redovi“.";

// Calendar period filter: quick ranges and a custom one (ISO dates, inclusive). An empty range = the default view
// (everything still open plus the next 60 days).
export type RangePreset = "default" | "thisMonth" | "nextMonth" | "quarter" | "year" | "custom";
export const rangePresets: { value: RangePreset; label: string }[] = [
  { value: "default", label: "Narednih 60 dana" }, { value: "thisMonth", label: "Ovaj mjesec" },
  { value: "nextMonth", label: "Sljedeći mjesec" }, { value: "quarter", label: "Tekući kvartal" },
  { value: "year", label: "Cijela godina" }, { value: "custom", label: "Proizvoljno" }];

const iso = (year: number, month: number, day: number) =>
  `${year}-${String(month + 1).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
const lastDay = (year: number, month: number) => new Date(Date.UTC(year, month + 1, 0)).getUTCDate();

export function rangeFor(preset: RangePreset, today: string, custom: { from: string; to: string } = { from: "", to: "" }):
  { from: string; to: string } {
  const [year, monthNumber] = today.split("-").map(Number);
  const month = monthNumber - 1;
  switch (preset) {
    case "thisMonth": return { from: iso(year, month, 1), to: iso(year, month, lastDay(year, month)) };
    case "nextMonth": {
      const nextYear = month === 11 ? year + 1 : year;
      const next = (month + 1) % 12;
      return { from: iso(nextYear, next, 1), to: iso(nextYear, next, lastDay(nextYear, next)) };
    }
    case "quarter": {
      const first = Math.floor(month / 3) * 3;
      return { from: iso(year, first, 1), to: iso(year, first + 2, lastDay(year, first + 2)) };
    }
    case "year": return { from: iso(year, 0, 1), to: iso(year, 11, 31) };
    case "custom": return custom;
    default: return { from: "", to: "" };
  }
}

export type CalendarFilters = { preset: RangePreset; from: string; to: string; status: string; category: string };
export const defaultCalendarFilters: CalendarFilters = { preset: "default", from: "", to: "", status: "", category: "" };

export function calendarQuery(filters: CalendarFilters, today: string): string {
  const range = rangeFor(filters.preset, today, { from: filters.from, to: filters.to });
  const params = new URLSearchParams();
  if (range.from) params.set("from", range.from);
  if (range.to) params.set("to", range.to);
  if (filters.status) params.set("status", filters.status);
  if (filters.category) params.set("category", filters.category);
  const query = params.toString();
  return query ? `/calendar?${query}` : "/calendar";
}

// "Pokreni radni nalog" is offered for occurrences that are stored, not done and have no work order yet.
export function canStartWorkOrder(item: { id: string | null; status: string; workOrderId: string | null; projected?: boolean }): boolean {
  return Boolean(item.id) && !item.projected && !item.workOrderId && (item.status === "Due" || item.status === "Planned");
}

// Home screen card text.
export type MaintenanceSummary = { due: number; inProgress: number; unreadTickets: number | null; nextDueOn: string | null };
export function summaryText(summary: MaintenanceSummary): string {
  return `Održavanje: ${summary.due} ${summary.due === 1 ? "dospjela obaveza" : "dospjelih obaveza"} / ${summary.inProgress} u toku`;
}
export function summaryTone(summary: MaintenanceSummary): "danger" | "warning" | "ok" {
  return summary.due > 0 ? "danger" : summary.inProgress > 0 || (summary.unreadTickets ?? 0) > 0 ? "warning" : "ok";
}
export const calendarStatusHandoffKey = "zev.maintenance.calendarStatus";
