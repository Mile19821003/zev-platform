import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { accessSourceList, membershipRoleLabels, roleLabel, roleLabels, roleList } from "../src/uiLabels.ts";
import { roleLabel as hodogramRoleLabel } from "../src/hodogramUi.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("roles are shown in Bosnian; unknown values pass through unchanged", () => {
  assert.deepEqual(["OrganizationAdministrator", "Manager", "Accountant", "BuildingRepresentative", "Tenant", "Resident", "SuperAdmin"].map(roleLabel),
    ["Administrator organizacije", "Upravnik", "Računovođa", "Predstavnik zgrade", "Stanar", "Stanar", "Sistemski administrator"]);
  assert.equal(roleLabel("Owner"), "Vlasnik");
  assert.equal(roleLabel("Auditor"), "Auditor");
  assert.equal(roleList(["Manager", "Accountant", "Manager"]), "Upravnik, Računovođa");
  assert.equal(membershipRoleLabels, roleLabels, "one mapping for the whole app");
  assert.equal(hodogramRoleLabel("BuildingRepresentative"), "Predstavnik zgrade");
});

test("access sources are named, not shown as codes", () => {
  assert.equal(accessSourceList(["ZEV_MEMBERSHIP", "OWNERSHIP", "MANUAL_OVERRIDE", "ORG_ROLE"]),
    "Članstvo u ZEV-u, Vlasništvo, Ručno dodijeljena prava, Uloga u organizaciji");
  assert.equal(accessSourceList(undefined), "");
});

test("forms and tables display role names but send and store the codes", () => {
  const workspace = source("../src/admin/Workspace.tsx");
  assert.ok(workspace.includes('options: ["OrganizationAdministrator", "Manager", "Accountant", "BuildingRepresentative"], optionLabels: roleLabels'));
  assert.ok(workspace.includes('{ key: "role", label: "Uloga", labels: roleLabels }'));
  const resource = source("../src/admin/ResourcePage.tsx");
  assert.ok(resource.includes("<option key={option} value={option}>{field.optionLabels?.[option] ?? option}</option>"), "value stays the code");
  assert.ok(resource.includes("column.labels[String(item[column.key])]"));
  const matrix = source("../src/admin/PermissionMatrix.tsx");
  assert.ok(matrix.includes("Uloga: {roleList(context.roles)") && matrix.includes("Izvor: {accessSourceList(context.sources)}"));
  assert.doesNotMatch(matrix, /context\.roles\.join|context\.sources\.join/);
  assert.ok(source("../src/admin/RoleWorkspace.tsx").includes("accessSourceList(zev.sources)"));
});

test("manual roles can be edited: Uredi fills the form, Ažuriraj sends PUT, Otkaži resets", () => {
  const workspace = source("../src/admin/Workspace.tsx");
  const memberships = workspace.slice(workspace.indexOf("memberships: {"), workspace.indexOf("};", workspace.indexOf("memberships: {")));
  assert.ok(memberships.includes("allowEdit: true, allowStatus: true"));
  const resource = source("../src/admin/ResourcePage.tsx");
  assert.ok(resource.includes('{busy ? "Čuvanje…" : editingId ? "Ažuriraj" : "Sačuvaj"}'));
  assert.ok(resource.includes('{editingId ? "Uredi zapis" : "Novi zapis"}'));
  assert.ok(resource.includes('method: editingId ? "PUT" : "POST"'));
  assert.ok(resource.includes('{editingId && <button className="secondary" type="button" onClick={reset}>Otkaži</button>}'));
  assert.ok(source("../../src/Zev.Api/Endpoints/AdminAccessEndpoints.cs").includes('group.MapPut("/memberships/{membershipId:guid}"'));
});
