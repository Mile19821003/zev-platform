import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { changeModule, datesValid, emptyRecycleFilters, formOptions, moduleOptions, recycleQuery, restoredMessage, shortId,
  withoutRecord } from "../src/recycleBinUi.ts";
import { iconCatalog } from "../src/navigationModel.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const types = [
  { entityType: "Building", module: "Registar", form: "Zgrade" },
  { entityType: "Entrance", module: "Registar", form: "Ulazi" },
  { entityType: "Supplier", module: "Finansije", form: "Dobavljači" },
];
const record = (entityType, recordId, label = "B1 · Zgrada") => ({ id: 7, entityType, module: "Registar", form: "Zgrade",
  recordId, label, deletedAt: "2026-10-11T08:00:00Z", deletedByUserId: null, deletedBy: "manager@test.invalid" });

test("module and form filters follow the types returned by the API", () => {
  assert.deepEqual(moduleOptions(types), ["Registar", "Finansije"]);
  assert.deepEqual(formOptions(types, "").map((type) => type.form), ["Zgrade", "Ulazi", "Dobavljači"]);
  assert.deepEqual(formOptions(types, "Finansije").map((type) => type.entityType), ["Supplier"]);
  const chosen = { ...emptyRecycleFilters, module: "Registar", entityType: "Entrance" };
  assert.equal(changeModule(chosen, "Finansije", types).entityType, "", "form of another module is cleared");
  assert.equal(changeModule(chosen, "", types).entityType, "Entrance", "all modules keep the form");
});

test("query string carries only the chosen filters", () => {
  assert.equal(recycleQuery(emptyRecycleFilters, 1), "/api/admin/recycle-bin?page=1");
  assert.equal(recycleQuery({ module: "Registar", entityType: "Building", from: "2026-10-01", to: "2026-10-11" }, 2),
    "/api/admin/recycle-bin?module=Registar&entityType=Building&from=2026-10-01&to=2026-10-11&page=2");
  assert.equal(datesValid({ ...emptyRecycleFilters, from: "2026-10-02", to: "2026-10-01" }), false);
  assert.equal(datesValid({ ...emptyRecycleFilters, from: "2026-10-02" }), true);
});

test("restored row leaves the list and the toast names the record", () => {
  const page = { page: 1, pageSize: 20, totalCount: 3, items: [record("Building", "a"), record("Entrance", "a"), record("Building", "b")] };
  const next = withoutRecord(page, page.items[0]);
  assert.deepEqual(next.items.map((item) => `${item.entityType}:${item.recordId}`), ["Entrance:a", "Building:b"]);
  assert.equal(next.totalCount, 2);
  assert.equal(withoutRecord(next, page.items[0]).totalCount, 2, "unknown row changes nothing");
  assert.equal(restoredMessage(page.items[0]), "Zgrade: „B1 · Zgrada“ je vraćen u sistem.");
  assert.equal(shortId("1c0e0000-0000-4000-8000-000000000037"), "1c0e0000…");
});

test("types offered by the screen equal the backend catalog", () => {
  const backend = source("../../src/Zev.Api/RecycleBin/SoftDeleteAdminService.cs");
  const catalog = [...backend.matchAll(/new\("(\w+)", "([^"]+)", "([^"]+)", typeof/g)].map((match) => match.slice(1, 4).join("|"));
  assert.deepEqual(catalog, ["Building|Registar|Zgrade", "Entrance|Registar|Ulazi", "PropertyUnit|Registar|Posebni dijelovi",
    "Person|Registar|Vlasnici / korisnici", "Supplier|Finansije|Dobavljači", "MaintenancePlan|Održavanje|Planovi održavanja"]);
});

test("Obrisani redovi is an administration module and delete actions are wired", () => {
  const entry = iconCatalog().find((item) => item.moduleKey === "deleted");
  assert.equal(entry?.category, "Administracija i sistem");
  assert.equal(entry?.label, "Obrisani redovi");
  assert.equal(entry?.defaultIcon, "package");
  const workspace = source("../src/admin/Workspace.tsx");
  assert.match(workspace, /active === "deleted" && <DeletedRecordsPage/);
  assert.equal((workspace.match(/allowDelete: true/g) ?? []).length, 4, "buildings, entrances, units, people");
  assert.match(source("../src/admin/ManagerRegistryPage.tsx"), /allowDelete: section !== "people"/);
  assert.match(source("../src/admin/ExpensesPage.tsx"), /expenses\/suppliers\/\$\{item.id\}`, \{ method: "DELETE" \}/);
  const page = source("../src/admin/DeletedRecordsPage.tsx");
  for (const header of ["ID", "Modul", "Forma", "ID zapisa", "Vrijeme brisanja", "Obrisao", "Akcija"])
    assert.ok(page.includes(`<th>${header}</th>`), header);
  assert.match(page, /Osvježi/);
  assert.match(page, /<option value="">Sve forme<\/option>/);
});
