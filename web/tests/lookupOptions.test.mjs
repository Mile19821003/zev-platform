import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { defaultValue, labelOf, lookupFallbacks, normalizeLookupResponse, reasonText, selectOptions }
  from "../src/lookupOptions.ts";
import { mutationKeys, resourceKey } from "../src/dataSync.ts";

// System seed of the code lists (src/Zev.Api/Domain/LookupEntities.cs): (type, code, name, default).
const backend = readFileSync(new URL("../../src/Zev.Api/Domain/LookupEntities.cs", import.meta.url), "utf8");
const seed = [...backend.matchAll(/\("([A-Z_]+)", "([^"]+)", "([^"]+)", (true|false)\)/g)]
  .map((match) => ({ type: match[1], code: match[2], name: match[3], isDefault: match[4] === "true" }));

test("fallback options equal the backend seed, in the same order", () => {
  assert.equal(seed.length, 34);
  for (const [type, items] of Object.entries(lookupFallbacks)) {
    const expected = seed.filter((row) => row.type === type);
    // Appended compatibility rows come last in the seed but belong at their sort position in the list.
    assert.deepEqual([...items].map((item) => item.code).sort(), expected.map((row) => row.code).sort(), type);
    for (const item of items) {
      const row = expected.find((entry) => entry.code === item.code);
      assert.deepEqual([item.name, item.isDefault], [row.name, row.isDefault], `${type}.${item.code}`);
    }
  }
});

test("enum-like fields store codes, free-text fields store names", () => {
  const units = selectOptions(lookupFallbacks.PROPERTY_UNIT_TYPE, "code");
  assert.deepEqual(units.map((option) => option.value),
    ["Apartment", "Commercial", "Garage", "ParkingSpace", "Basement", "Other"]);
  assert.equal(units[0].label, "Stan");
  const categories = selectOptions(lookupFallbacks.EXPENSE_CATEGORY, "name");
  assert.deepEqual(categories[0], { value: "Tekuće održavanje", label: "Tekuće održavanje" });
  assert.equal(defaultValue(lookupFallbacks.MAINTENANCE_PRIORITY, "code"), "Normal");
  assert.equal(defaultValue(lookupFallbacks.EXPENSE_CATEGORY, "name"), "");
});

test("an existing value that is no longer offered stays selectable", () => {
  const options = selectOptions(lookupFallbacks.MAINTENANCE_CATEGORY, "name", "Rasvjeta");
  assert.deepEqual(options.at(-1), { value: "Rasvjeta", label: "Rasvjeta (postojeća vrijednost)" });
  const deactivated = selectOptions(lookupFallbacks.PROPERTY_UNIT_TYPE.filter((item) => item.code !== "Basement"),
    "code", "Basement");
  assert.deepEqual(deactivated.at(-1), { value: "Basement", label: "Basement (postojeća vrijednost)" });
  assert.equal(selectOptions(lookupFallbacks.PROPERTY_UNIT_TYPE, "code", "Garage").length, 6);
});

test("cash is offered only when manual cash payments are allowed", () => {
  const allowCash = (allowed) => (item) => item.code !== "Cash" || allowed;
  assert.deepEqual(selectOptions(lookupFallbacks.PAYMENT_METHOD, "code", "", allowCash(false)).map((option) => option.value),
    ["Transfer", "PaymentSlip", "Migration", "Other"]);
  assert.ok(selectOptions(lookupFallbacks.PAYMENT_METHOD, "code", "", allowCash(true)).some((option) => option.value === "Cash"));
});

test("labels, storno reason text and API response", () => {
  assert.equal(labelOf(lookupFallbacks.MAINTENANCE_PRIORITY, "Urgent"), "Hitno");
  assert.equal(labelOf(lookupFallbacks.MAINTENANCE_PRIORITY, "Nepoznato"), "Nepoznato");
  assert.equal(reasonText("Pogrešan unos", "  duplo knjiženo "), "Pogrešan unos: duplo knjiženo");
  assert.equal(reasonText("Reklamacija", "  "), "Reklamacija");
  assert.deepEqual(normalizeLookupResponse({ type: {}, items: [{ id: "x", code: "A", name: "Ana", isDefault: true },
    { code: 3 }] }), [{ code: "A", name: "Ana", isDefault: true }]);
  assert.equal(normalizeLookupResponse({ items: "x" }), null);
});

test("administrator edits of code lists notify the forms", () => {
  assert.equal(resourceKey("/api/admin/lookups/items/1"), "lookups");
  assert.deepEqual(mutationKeys("/api/admin/lookups/EXPENSE_CATEGORY/items"), ["lookups"]);
});
