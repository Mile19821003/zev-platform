export type RaciValue = "R" | "A" | "C" | "I";
export type HodogramStep = { id: string; name: string; description: string | null; sortOrder: number;
  cells: Record<string, RaciValue> };
export type HodogramPhase = { id: string; name: string; sortOrder: number; steps: HodogramStep[] };
export type RaciIssue = { stepId: string; code: string; message: string };
export type HodogramDetail = { id: string; organizationId: string; name: string; description: string | null;
  isActive: boolean; revision: number; updatedUtc: string; roles: string[]; phases: HodogramPhase[];
  isValid: boolean; issues: RaciIssue[] };
export type HodogramSummary = { id: string; name: string; description: string | null; isActive: boolean;
  revision: number; phaseCount: number; stepCount: number };
export type MatrixDraft = Record<string, Record<string, RaciValue | "">>;

export const raciValues: RaciValue[] = ["R", "A", "C", "I"];

export const raciLabels: Record<RaciValue, string> = {
  R: "R – izvršava", A: "A – odgovoran", C: "C – konsultuje se", I: "I – informiše se",
};

export const roleLabels: Record<string, string> = {
  Manager: "Upravnik", Accountant: "Računovođa", BuildingRepresentative: "Predstavnik zgrade",
  Owner: "Vlasnik", OrganizationAdministrator: "Administrator organizacije",
};

export function roleLabel(role: string): string {
  return roleLabels[role] ?? role;
}

export function draftFromDetail(detail: HodogramDetail): MatrixDraft {
  return Object.fromEntries(detail.phases.flatMap((phase) => phase.steps)
    .map((step) => [step.id, { ...step.cells }]));
}

// Mirrors the server rule: at least one R and exactly one A per step; C and I are optional.
export function validateStep(cells: Record<string, RaciValue | ""> | undefined): string[] {
  const values = Object.values(cells ?? {});
  const problems: string[] = [];
  if (!values.includes("R")) problems.push("Nedostaje R (izvršava).");
  const accountable = values.filter((value) => value === "A").length;
  if (accountable === 0) problems.push("Nedostaje A (odgovoran).");
  if (accountable > 1) problems.push("Samo jedna uloga smije biti A.");
  return problems;
}

export function draftIssues(detail: HodogramDetail, draft: MatrixDraft): Record<string, string[]> {
  return Object.fromEntries(detail.phases.flatMap((phase) => phase.steps)
    .map((step) => [step.id, validateStep(draft[step.id])] as const)
    .filter(([, problems]) => problems.length > 0));
}

export function issuesByStep(issues: RaciIssue[] | undefined): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  for (const issue of issues ?? []) (result[issue.stepId] ??= []).push(issue.message);
  return result;
}

export function matrixPayload(detail: HodogramDetail, draft: MatrixDraft) {
  return {
    revision: detail.revision,
    cells: detail.phases.flatMap((phase) => phase.steps).flatMap((step) => detail.roles
      .map((role) => ({ stepId: step.id, role, value: draft[step.id]?.[role] ?? "" }))
      .filter((cell) => cell.value !== "")),
  };
}

export function isDraftChanged(detail: HodogramDetail, draft: MatrixDraft): boolean {
  return detail.phases.flatMap((phase) => phase.steps).some((step) => detail.roles
    .some((role) => (step.cells[role] ?? "") !== (draft[step.id]?.[role] ?? "")));
}
