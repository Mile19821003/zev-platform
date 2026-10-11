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

// Registry codes (PersonKind, OccupantKind) and the active filter; API and database keep the codes.
export const personKindLabels: Record<string, string> = { Individual: "Fizičko lice", LegalEntity: "Pravno lice" };
export const occupantKindLabels: Record<string, string> = { Resident: "Stanovnik", Tenant: "Podstanar", OtherUser: "Drugi korisnik" };
export const activeFilterLabels: Record<string, string> = { true: "Aktivni", false: "Neaktivni" };

// Finance codes: expenses, legal, bank and payments. Requests and the database keep the codes.
export const expenseStatusLabels: Record<string, string> = { Draft: "Nacrt", Posted: "Proknjiženo", Reversed: "Stornirano" };
export const reminderKindLabels: Record<string, string> = { Payment: "Opomena", BeforeLawsuit: "Opomena pred utuženje" };
export const legalCaseStatusLabels: Record<string, string> = {
  Open: "Otvoren", Filed: "Tužba podnesena", InProgress: "U toku", Settled: "Riješen nagodbom", Closed: "Zatvoren",
};
export const bankTransactionStatusLabels: Record<string, string> = {
  Pending: "Na čekanju", Matched: "Povezano", ManualReview: "Za provjeru", Ignored: "Zanemareno",
  PartiallyMatched: "Djelimično povezano", Returned: "Vraćeno",
};
export const bankImportStatusLabels: Record<string, string> = {
  Imported: "Uvezen", PartiallyReconciled: "Djelimično usklađen", Reconciled: "Usklađen",
};
export const paymentSourceLabels: Record<string, string> = { BankTransaction: "Bankovni izvod", Manual: "Ručna uplata" };
export const fundKindLabels: Record<string, string> = { Regular: "Redovni", Reserve: "Rezervni" };

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
