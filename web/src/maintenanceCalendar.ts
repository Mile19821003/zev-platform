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
