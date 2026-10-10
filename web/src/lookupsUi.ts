// View rules for the code lists screen (LookupsPage.tsx) over GET /api/admin/lookups.
export type LookupItem = { id: string; code: string; name: string; sortOrder: number; isActive: boolean;
  isDefault: boolean; zevId: string | null; updatedUtc: string | null };
export type LookupType = { code: string; name: string; description: string | null; isSystem: boolean; items: LookupItem[] };
export type LookupDraft = { id: string | null; code: string; name: string; sortOrder: string; isActive: boolean; isDefault: boolean };

const plain = (text: string) => text.toLowerCase().replace(/đ/g, "d").normalize("NFD").replace(/[̀-ͯ]/g, "");

// Quick search over list name, code and description, ignoring case and diacritics ("sifarnik" finds "Šifarnik").
export function filterTypes(types: LookupType[], query: string): LookupType[] {
  const needle = plain(query.trim());
  if (!needle) return types;
  return types.filter((type) => [type.name, type.code, type.description ?? ""].some((text) => plain(text).includes(needle)));
}

export function itemCountLabel(type: LookupType): string {
  const active = type.items.filter((item) => item.isActive).length;
  const total = type.items.length;
  return active === total ? `${total}` : `${active}/${total}`;
}

export function statusLabel(item: LookupItem): string {
  return item.isActive ? item.isDefault ? "Aktivna · zadana" : "Aktivna" : "Neaktivna";
}

export function nextSortOrder(items: LookupItem[]): number {
  return Math.max(0, ...items.map((item) => item.sortOrder)) + 10;
}

export function emptyDraft(type: LookupType): LookupDraft {
  return { id: null, code: "", name: "", sortOrder: String(nextSortOrder(type.items)), isActive: true, isDefault: false };
}

export function draftOf(item: LookupItem): LookupDraft {
  return { id: item.id, code: item.code, name: item.name, sortOrder: String(item.sortOrder), isActive: item.isActive,
    isDefault: item.isDefault };
}

// Same rules as the API, checked before sending so the form can explain the problem.
export function validateDraft(draft: LookupDraft, type: LookupType): string | null {
  if (draft.id === null) {
    if (!/^[A-Za-z0-9_-]{1,50}$/.test(draft.code.trim())) return "Šifra: slova, brojevi, - i _, do 50 znakova.";
    if (type.items.some((item) => item.zevId === null && item.code === draft.code.trim()))
      return "Stavka s tom šifrom već postoji u šifarniku.";
  }
  const name = draft.name.trim();
  if (name.length === 0 || name.length > 200) return "Naziv je obavezan (do 200 znakova).";
  const order = Number(draft.sortOrder);
  if (!Number.isInteger(order) || order < 0 || order > 100000) return "Redoslijed je cijeli broj od 0 do 100000.";
  if (draft.isDefault && !draft.isActive) return "Neaktivna stavka ne može biti zadana vrijednost.";
  return null;
}

// Request body for POST (new item) or PUT (existing item).
export function requestBody(draft: LookupDraft): Record<string, unknown> {
  const common = { name: draft.name.trim(), sortOrder: Number(draft.sortOrder), isDefault: draft.isDefault };
  return draft.id === null ? { ...common, code: draft.code.trim() } : { ...common, isActive: draft.isActive };
}

// Toggling the status of a default item also drops the default flag (the API rejects an inactive default).
export function toggledBody(item: LookupItem): Record<string, unknown> {
  return { name: item.name, sortOrder: item.sortOrder, isActive: !item.isActive, isDefault: item.isActive ? false : item.isDefault };
}
