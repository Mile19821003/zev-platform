import assert from "node:assert/strict";
import test from "node:test";
import { draftOf, emptyDraft, filterTypes, itemCountLabel, nextSortOrder, requestBody, statusLabel, toggledBody,
  validateDraft } from "../src/lookupsUi.ts";
import { featuredModules, groupNavigation } from "../src/navigationModel.ts";

const item = (code, name, sortOrder, extra = {}) => ({ id: `id-${code}`, code, name, sortOrder, isActive: true,
  isDefault: false, zevId: null, updatedUtc: null, ...extra });
const priorities = { code: "MAINTENANCE_PRIORITY", name: "Prioriteti održavanja", description: "Hitnost prijave kvara",
  isSystem: true, items: [item("Urgent", "Hitno", 10), item("High", "Visok", 20),
    item("Normal", "Standardni", 30, { isDefault: true }), item("Low", "Nizak", 40, { isActive: false })] };
const methods = { code: "PAYMENT_METHOD", name: "Načini plaćanja", description: null, isSystem: true,
  items: [item("Cash", "Gotovina", 10)] };

test("quick search finds lists by name, code or description, ignoring case and diacritics", () => {
  assert.deepEqual(filterTypes([priorities, methods], "nacini").map((type) => type.code), ["PAYMENT_METHOD"]);
  assert.deepEqual(filterTypes([priorities, methods], "maintenance").map((type) => type.code), ["MAINTENANCE_PRIORITY"]);
  assert.deepEqual(filterTypes([priorities, methods], "HITNOST").map((type) => type.code), ["MAINTENANCE_PRIORITY"]);
  assert.equal(filterTypes([priorities, methods], "  ").length, 2);
  assert.equal(filterTypes([priorities, methods], "nema").length, 0);
});

test("counter, status labels and next order", () => {
  assert.equal(itemCountLabel(priorities), "3/4");
  assert.equal(itemCountLabel(methods), "1");
  assert.deepEqual(priorities.items.map(statusLabel), ["Aktivna", "Aktivna", "Aktivna · zadana", "Neaktivna"]);
  assert.equal(nextSortOrder(priorities.items), 50);
  assert.equal(nextSortOrder([]), 10);
});

test("the form validates like the API before sending", () => {
  const draft = { ...emptyDraft(priorities), code: "Critical", name: " Kritično " };
  assert.equal(draft.sortOrder, "50");
  assert.equal(validateDraft(draft, priorities), null);
  assert.deepEqual(requestBody(draft), { name: "Kritično", sortOrder: 50, isDefault: false, code: "Critical" });
  assert.match(validateDraft({ ...draft, code: "loša šifra" }, priorities), /Šifra/);
  assert.match(validateDraft({ ...draft, code: "Urgent" }, priorities), /već postoji/);
  assert.match(validateDraft({ ...draft, name: "  " }, priorities), /Naziv/);
  assert.match(validateDraft({ ...draft, sortOrder: "1.5" }, priorities), /Redoslijed/);
  const editing = draftOf(priorities.items[2]);
  assert.deepEqual(requestBody({ ...editing, name: "Normalan" }), { name: "Normalan", sortOrder: 30, isDefault: true, isActive: true });
  assert.match(validateDraft({ ...editing, isActive: false }, priorities), /Neaktivna stavka/);
  // An existing code is not re-checked when editing (the code is read-only).
  assert.equal(validateDraft(editing, priorities), null);
});

test("deactivating a default item also clears the default flag; reactivating keeps the rest", () => {
  assert.deepEqual(toggledBody(priorities.items[2]), { name: "Standardni", sortOrder: 30, isActive: false, isDefault: false });
  assert.deepEqual(toggledBody(priorities.items[3]), { name: "Nizak", sortOrder: 40, isActive: true, isDefault: false });
});

test("Šifarnici belong to administration and get a quick-access card on the home grid", () => {
  const menu = [{ key: "overview", label: "Pregled" }, { key: "lookups", label: "Šifarnici" }, { key: "users", label: "Korisnici" }];
  assert.deepEqual(groupNavigation(menu).map((group) => [group.id, group.items.map((entry) => entry.key)]),
    [["admin", ["lookups", "users"]]]);
  assert.deepEqual(featuredModules(menu), [{ key: "lookups", label: "Šifarnici", icon: "barcode" }]);
  assert.deepEqual(featuredModules([{ key: "users", label: "Korisnici" }]), []);
});
