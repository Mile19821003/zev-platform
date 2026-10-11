import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { cellState, cellView, columnLabels, visibleColumns } from "../src/permissionMatrixUi.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const catalog = source("../../src/Zev.Api/Security/GranularPermissions.cs");

// Modules of the backend catalog: new("Name", "Label", [actions], [details]).
const modules = [...catalog.matchAll(/new\("(\w+)", "([^"]+)", \[([^\]]*)\],\s*\[([^\]]*)\]\)/g)].map((match) => ({
  name: match[1], label: match[2],
  actions: [...match[3].matchAll(/"(\w+)"/g)].map((item) => item[1]),
  details: [...match[4].matchAll(/"([\w.]+)"/g)].map((item) => item[1]),
}));

test("the catalog has separate delete permissions; the matrix shows the Brisanje column again", () => {
  const details = modules.flatMap((module) => module.details);
  for (const key of ["Buildings.Delete", "PropertyUnits.Delete", "Suppliers.Delete"]) assert.ok(details.includes(key), key);
  assert.ok(modules.find((module) => module.name === "Maintenance").actions.includes("Delete"));
  assert.deepEqual(visibleColumns(modules), ["View", "Create", "Edit", "Delete", "Approve", "Export"]);
  assert.equal(columnLabels.Delete, "Brisanje");
  assert.equal(cellView(cellState({ key: "Maintenance.Delete", roleDefault: true, override: null, effective: true }, {}, "Maintenance.Delete"), "Upravnik").glyph, "✓");
  assert.equal(cellView(cellState({ key: "Maintenance.Delete", roleDefault: false, override: "Grant", effective: true }, {}, "Maintenance.Delete"), "Predstavnik").glyph, "✓+");
  assert.equal(cellView(cellState({ key: "Maintenance.Delete", roleDefault: false, override: null, effective: false }, {}, "Maintenance.Delete"), "Računovođa").glyph, "□");
  assert.equal(cellView(cellState({ key: "Maintenance.Delete", roleDefault: true, override: "Deny", effective: false }, {}, "Maintenance.Delete"), "Upravnik").glyph, "✕");
});

test("Obriši buttons follow the delete permissions, not the edit ones", () => {
  const registry = source("../src/admin/ManagerRegistryPage.tsx");
  assert.ok(registry.includes('allowDelete: section !== "people" && allowed(({ buildings: "Buildings.Delete", entrances: "PropertyUnits.Delete",'));
  assert.ok(registry.includes('units: "PropertyUnits.Delete"'));
  assert.ok(source("../src/admin/ExpensesPage.tsx").includes('{allowed("Suppliers.Delete") && <button type="button" className="danger"'));
  const maintenance = source("../src/admin/MaintenancePage.tsx");
  assert.ok(maintenance.includes('allowed("Maintenance.Delete")\n              && <button type="button" className="danger"')
    || maintenance.includes('allowed("Maintenance.Delete")\r\n              && <button type="button" className="danger"'));
  const routes = source("../../src/Zev.Api/Endpoints/RecycleBinEndpoints.cs");
  for (const key of ["Buildings.Delete", "PropertyUnits.Delete", "Suppliers.Delete"]) assert.ok(routes.includes(`"${key}"`), key);
  assert.doesNotMatch(routes, /RequirePermissionAsync\(zevId, "(Buildings|Entrances|PropertyUnits)\.Edit"/);
});
