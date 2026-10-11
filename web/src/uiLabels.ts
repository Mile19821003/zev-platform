// Bosnian display labels for API enum values; unknown values are shown unchanged.
export const documentTypeLabels: Record<string, string> = {
  Contract: "Ugovor", Policy: "Pravilnik", Statute: "Statut", Minutes: "Zapisnik",
  Financial: "Finansijski", Legal: "Pravni", Other: "Ostalo", InsurancePolicy: "Polisa osiguranja", Decision: "Odluka",
};

export const visibilityLabels: Record<string, string> = {
  ManagersOnly: "Samo upravnik", ZevMembers: "Članovi ZEV-a", PersonOnly: "Samo lice",
  PropertyUnit: "Vlasnici jedinice", Role: "Uloga",
};

export const announcementScopeLabels: Record<string, string> = {
  Zev: "Cijeli ZEV", Building: "Zgrada", Entrance: "Ulaz", Role: "Uloga",
};

// Role names shown in the UI. Only the display changes: API requests and the database keep the English codes.
export const roleLabels: Record<string, string> = {
  OrganizationAdministrator: "Administrator organizacije", Manager: "Upravnik", Accountant: "Računovođa",
  BuildingRepresentative: "Predstavnik zgrade", Owner: "Vlasnik", Tenant: "Stanar", Resident: "Stanar",
  SuperAdmin: "Sistemski administrator", SystemAdministrator: "Sistemski administrator",
};
export const membershipRoleLabels = roleLabels;

export function roleLabel(role: string): string {
  return roleLabels[role] ?? role;
}

export function roleList(roles: string[]): string {
  return [...new Set(roles.map(roleLabel))].join(", ");
}

// Where a user's access to a ZEV comes from (EffectiveAccessService sources).
export const accessSourceLabels: Record<string, string> = {
  ORG_ROLE: "Uloga u organizaciji", ZEV_MEMBERSHIP: "Članstvo u ZEV-u", OWNERSHIP: "Vlasništvo",
  MANUAL_OVERRIDE: "Ručno dodijeljena prava",
};

export function accessSourceList(sources: string[] | undefined): string {
  return (sources ?? []).map((source) => accessSourceLabels[source] ?? roleLabel(source)).join(", ");
}

export const ballotKindLabels: Record<string, string> = { Survey: "Anketa", Formal: "Formalno glasanje" };
export const ballotModeLabels: Record<string, string> = { OneUnitOneVote: "Jedan stan = jedan glas", Weighted: "Ponderisano" };
export const ballotStatusLabels: Record<string, string> = { Draft: "Nacrt", Open: "Otvoreno", Closed: "Zaključeno" };

export const maintenanceStatusLabels: Record<string, string> = {
  Open: "Otvoreno", InProgress: "U toku", Completed: "Završeno", Verified: "Potvrđeno", Cancelled: "Otkazano",
  Due: "Dospjelo", Planned: "Planirano",
};

export const priorityLabels: Record<string, string> = { Low: "Nizak", Normal: "Normalan", High: "Visok", Urgent: "Hitno" };

export function label(labels: Record<string, string>, value: string | null | undefined): string {
  return value ? labels[value] ?? value : "";
}
