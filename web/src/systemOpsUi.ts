// View rules for the system error log and version history screens.
export type ErrorFilter = "active" | "resolved" | "all";
export type SystemError = { id: number; title: string; source: "Frontend" | "Backend"; errorType: string; message: string;
  stackTrace: string | null; requestPath: string | null; userId: string | null; status: "Active" | "Resolved";
  adminNotes: string | null; createdAt: string; resolvedAt: string | null; resolvedByUserId: string | null };
export type AppVersion = { version: string; status: string; title: string; installedAt: string; installedBy: string;
  adminNotes: string | null; notesUpdatedAt: string | null };

export const errorFilters: { value: ErrorFilter; label: string }[] = [
  { value: "active", label: "Aktuelni" }, { value: "resolved", label: "Završeni" }, { value: "all", label: "Svi" }];

export function sourceBadge(source: string): { label: string; className: string } {
  return source === "Frontend" ? { label: "Frontend", className: "badge frontend" } : { label: "Backend", className: "badge backend" };
}

export function errorStatusLabel(status: string): string {
  return status === "Resolved" ? "Završen" : "Aktuelan";
}

// The detail form sends the checkbox and the note; nothing to save while both equal the stored values.
export function errorDraftChanged(error: SystemError, resolved: boolean, notes: string): boolean {
  return resolved !== (error.status === "Resolved") || notes.trim() !== (error.adminNotes ?? "");
}

// "v2.10" after "v2.9": numeric, newest first (the API sorts the same way).
export function sortVersions(versions: AppVersion[]): AppVersion[] {
  const parse = (version: string) => version.replace(/^v/i, "").split(".").map((part) => Number(part) || 0);
  return [...versions].sort((left, right) => {
    const [a, b] = [parse(left.version), parse(right.version)];
    for (let index = 0; index < Math.max(a.length, b.length); index++)
      if ((a[index] ?? 0) !== (b[index] ?? 0)) return (b[index] ?? 0) - (a[index] ?? 0);
    return 0;
  });
}

export function versionNoteChanged(version: AppVersion, draft: string | undefined): boolean {
  return draft !== undefined && draft.trim() !== (version.adminNotes ?? "");
}

export function versionStatusLabel(status: string): string {
  return status === "installed" ? "Instalirana" : status;
}
