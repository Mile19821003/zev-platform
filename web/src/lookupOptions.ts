// Options of form drop-downs from code lists (GET /api/lookups/{typeCode}). The fallback lists equal the system
// seed (src/Zev.Api/Domain/LookupEntities.cs, checked by a web test) and are used until the API answers or when it fails.
export type LookupTypeCode = "PROPERTY_UNIT_TYPE" | "EXPENSE_CATEGORY" | "MAINTENANCE_CATEGORY" | "MAINTENANCE_PRIORITY"
  | "DOCUMENT_CATEGORY" | "PAYMENT_METHOD" | "REVERSAL_REASON";
export type LookupOption = { code: string; name: string; isDefault: boolean };
export type SelectOption = { value: string; label: string };

const rows = (items: [string, string, boolean?][]): LookupOption[] =>
  items.map(([code, name, isDefault]) => ({ code, name, isDefault: isDefault === true }));

export const lookupFallbacks: Record<LookupTypeCode, LookupOption[]> = {
  PROPERTY_UNIT_TYPE: rows([["Apartment", "Stan", true], ["Commercial", "Poslovni prostor"], ["Garage", "Garaža"],
    ["ParkingSpace", "Parking mjesto"], ["Basement", "Ostava"], ["Other", "Ostalo"]]),
  EXPENSE_CATEGORY: rows([["CURRENT_MAINTENANCE", "Tekuće održavanje"], ["CAPITAL_MAINTENANCE", "Investiciono održavanje"],
    ["UTILITIES", "Komunalne usluge"], ["ELEVATOR", "Lift"], ["MANAGER", "Upravnik"]]),
  MAINTENANCE_CATEGORY: rows([["PLUMBING", "Vodovod"], ["ELECTRICAL", "Elektroinstalacije"],
    ["CONSTRUCTION", "Građevinski radovi"], ["ROOF", "Krov"], ["LOCKSMITH", "Bravarija"]]),
  MAINTENANCE_PRIORITY: rows([["Urgent", "Hitno"], ["High", "Visok"], ["Normal", "Standardni", true], ["Low", "Nizak"]]),
  DOCUMENT_CATEGORY: rows([["Minutes", "Zapisnik"], ["Contract", "Ugovor"], ["INSURANCE_POLICY", "Polisa osiguranja"],
    ["DECISION", "Odluka"], ["Financial", "Finansijski izvještaj"]]),
  PAYMENT_METHOD: rows([["Cash", "Gotovina"], ["Transfer", "Virman", true], ["PaymentSlip", "Uplatnica"],
    ["Migration", "Migraciona uplata"], ["Other", "Drugo"]]),
  REVERSAL_REASON: rows([["WRONG_ENTRY", "Pogrešan unos"], ["DUPLICATE", "Dvostruka uplata"], ["COMPLAINT", "Reklamacija"],
    ["AMOUNT_CORRECTION", "Ispravak iznosa"]]),
};

// The value stored by the form: an enum-like code (unit type, priority, payment method) or the readable name for
// free-text fields (expense and maintenance category), which keeps existing records and reports readable.
export type ValueBy = "code" | "name";

// A stored value that is no longer offered (deactivated item, older free text) stays selectable, so editing an old
// record never silently changes it.
export function selectOptions(items: LookupOption[], valueBy: ValueBy, current?: string | null,
  accept: (item: LookupOption) => boolean = () => true): SelectOption[] {
  const options = items.filter(accept).map((item) => ({ value: valueBy === "code" ? item.code : item.name, label: item.name }));
  if (current && !options.some((option) => option.value === current))
    options.push({ value: current, label: `${labelOf(items, current)} (postojeća vrijednost)` });
  return options;
}

export function defaultValue(items: LookupOption[], valueBy: ValueBy, accept: (item: LookupOption) => boolean = () => true): string {
  const item = items.filter(accept).find((entry) => entry.isDefault);
  return item ? valueBy === "code" ? item.code : item.name : "";
}

// Readable name of a stored code (tables); unknown values are shown unchanged.
export function labelOf(items: LookupOption[], value: string | null | undefined): string {
  if (!value) return "";
  return items.find((item) => item.code === value)?.name ?? value;
}

// Storno / correction reason: the chosen reason, plus an optional note ("Pogrešan unos: duplo knjiženo").
export function reasonText(reason: string, note: string): string {
  const trimmed = note.trim();
  return trimmed ? `${reason}: ${trimmed}` : reason;
}

export function normalizeLookupResponse(raw: unknown): LookupOption[] | null {
  const items = typeof raw === "object" && raw !== null ? (raw as { items?: unknown }).items : null;
  if (!Array.isArray(items)) return null;
  return items.filter((item): item is Record<string, unknown> => typeof item === "object" && item !== null
      && typeof item.code === "string" && typeof item.name === "string")
    .map((item) => ({ code: item.code as string, name: item.name as string, isDefault: item.isDefault === true }));
}
