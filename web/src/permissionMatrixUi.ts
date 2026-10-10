// Display rules for the permission matrix (PermissionMatrix.tsx): cell state, glyph and visible columns.
export type Effect = "Grant" | "Deny" | null;
export type PermissionState = { key: string; roleDefault: boolean; override: Effect; effective: boolean };
export type MatrixModule = { name: string; label: string; actions: string[]; details: string[] };
export type CellState = { effect: Effect; inherited: boolean; effective: boolean; changed: boolean };
export type CellView = { glyph: string; className: string; label: string };

export const matrixColumns = ["View", "Create", "Edit", "Delete", "Approve", "Export"];
export const columnLabels: Record<string, string> = { View: "Pregled", Create: "Kreiranje", Edit: "Izmjena",
  Delete: "Brisanje", Approve: "Odobravanje", Export: "Export" };

// Effective value of one key: saved state, or the unsaved draft applied on top of the role default.
export function cellState(base: PermissionState | undefined, draft: Record<string, Effect>, key: string): CellState {
  const changed = Object.prototype.hasOwnProperty.call(draft, key) && draft[key] !== (base?.override ?? null);
  const effect = Object.prototype.hasOwnProperty.call(draft, key) ? draft[key] : base?.override ?? null;
  const inherited = base?.roleDefault ?? false;
  const effective = !Object.prototype.hasOwnProperty.call(draft, key) ? base?.effective ?? false
    : effect === "Deny" ? false : effect === "Grant" ? true : inherited;
  return { effect, inherited, effective, changed };
}

// ✓ inherited from the role, ✓+ manual GRANT, ✕ manual DENY, □ not allowed.
export function cellView(state: CellState, roleName: string): CellView {
  const pending = state.changed ? " (nesačuvano)" : "";
  const changed = state.changed ? " changed" : "";
  if (state.effect === "Deny") return { glyph: "✕", className: `denied${changed}`,
    label: `Ručno zabranjeno${state.inherited ? ` (uloga ${roleName} bi dozvolila)` : ""}${pending}` };
  if (state.effect === "Grant") return { glyph: "✓+", className: `granted${changed}`,
    label: `Ručno dozvoljeno${state.inherited ? ` (uloga ${roleName} već dozvoljava)` : ""}${pending}` };
  if (state.effective) return { glyph: "✓", className: `inherited${changed}`,
    label: `Naslijeđeno iz uloge ${roleName}${pending}` };
  return { glyph: "□", className: `none${changed}`, label: `Nije dozvoljeno ulogom ${roleName}${pending}` };
}

// Click cycle: a manual override goes back to the role default; the default flips to the opposite override.
export function nextEffect(state: CellState): Effect {
  if (state.effect !== null) return null;
  return state.effective ? "Deny" : "Grant";
}

// Only columns that at least one module uses (no module has a Delete action today).
export function visibleColumns(modules: MatrixModule[]): string[] {
  return matrixColumns.filter((column) => modules.some((module) => module.actions.includes(column)));
}

// Changes worth sending: draft entries that differ from the saved override. A reset of a key without an
// override is not a change (sending it could also hit the grantor check for rights the actor lacks).
export function pendingChanges(permissions: PermissionState[], draft: Record<string, Effect>): { key: string; effect: Effect }[] {
  return Object.entries(draft).filter(([key, effect]) =>
    effect !== (permissions.find((item) => item.key === key)?.override ?? null))
    .map(([key, effect]) => ({ key, effect }));
}
