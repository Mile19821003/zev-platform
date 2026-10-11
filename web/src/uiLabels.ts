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

export const membershipRoleLabels: Record<string, string> = {
  Owner: "Vlasnik", Manager: "Upravnik", Accountant: "Računovođa",
  BuildingRepresentative: "Predstavnik zgrade", OrganizationAdministrator: "Administrator organizacije",
};

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
