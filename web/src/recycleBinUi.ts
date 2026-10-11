// View rules for Administration → "Obrisani redovi" (GET /api/admin/recycle-bin, POST .../restore).
export type DeletedType = { entityType: string; module: string; form: string };
export type DeletedRecord = { id: number | null; entityType: string; module: string; form: string; recordId: string;
  label: string; deletedAt: string | null; deletedByUserId: string | null; deletedBy: string | null };
export type RecycleFilters = { module: string; entityType: string; from: string; to: string };

export const emptyRecycleFilters: RecycleFilters = { module: "", entityType: "", from: "", to: "" };

export function moduleOptions(types: DeletedType[]): string[] {
  return [...new Set(types.map((type) => type.module))];
}

// Forms of the chosen module (all forms when "Sve forme" / no module is chosen).
export function formOptions(types: DeletedType[], module: string): DeletedType[] {
  return types.filter((type) => !module || type.module === module);
}

// A form that does not belong to the newly chosen module is cleared.
export function changeModule(filters: RecycleFilters, module: string, types: DeletedType[]): RecycleFilters {
  const keep = formOptions(types, module).some((type) => type.entityType === filters.entityType);
  return { ...filters, module, entityType: keep ? filters.entityType : "" };
}

export function recycleQuery(filters: RecycleFilters, page: number): string {
  const params = new URLSearchParams();
  if (filters.module) params.set("module", filters.module);
  if (filters.entityType) params.set("entityType", filters.entityType);
  if (filters.from) params.set("from", filters.from);
  if (filters.to) params.set("to", filters.to);
  params.set("page", String(page));
  return `/api/admin/recycle-bin?${params.toString()}`;
}

export function datesValid(filters: RecycleFilters): boolean {
  return !filters.from || !filters.to || filters.from <= filters.to;
}

export function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}

export function restoredMessage(record: DeletedRecord): string {
  return `${record.form}: „${record.label}“ je vraćen u sistem.`;
}

// After a restore the row leaves the list and the total drops by one, without waiting for a reload.
export function withoutRecord<T extends { totalCount: number; items: DeletedRecord[] }>(page: T, record: DeletedRecord): T {
  const items = page.items.filter((item) => !(item.entityType === record.entityType && item.recordId === record.recordId));
  return { ...page, items, totalCount: Math.max(0, page.totalCount - (page.items.length - items.length)) };
}
