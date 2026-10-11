import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { activeFilterLabels, label, occupantKindLabels, personKindLabels } from "../src/uiLabels.ts";
import { lookupFallbacks } from "../src/lookupOptions.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const enumMembers = (name) => {
  const domain = source("../../src/Zev.Api/Domain/RegistryEntities.cs");
  const body = domain.slice(domain.indexOf(`public enum ${name}`)).match(/\{([^}]*)\}/)[1];
  return body.split("\n").map((line) => line.replace(/\/\/.*/, "").trim().replace(/,$/, "")).flatMap((line) => line.split(/,\s*/)).filter(Boolean);
};

test("registry codes have Bosnian labels for every backend enum member", () => {
  assert.deepEqual(Object.keys(personKindLabels).sort(), enumMembers("PersonKind").sort());
  assert.deepEqual(Object.keys(occupantKindLabels).sort(), enumMembers("OccupantKind").sort());
  assert.deepEqual(lookupFallbacks.PROPERTY_UNIT_TYPE.map((item) => item.code).sort(), enumMembers("PropertyUnitType").sort());
  assert.deepEqual([label(personKindLabels, "Individual"), label(personKindLabels, "LegalEntity"), label(occupantKindLabels, "Tenant")],
    ["Fizičko lice", "Pravno lice", "Podstanar"]);
  assert.deepEqual(activeFilterLabels, { true: "Aktivni", false: "Neaktivni" });
});

test("registry pages show labels but keep sending codes", () => {
  for (const page of ["../src/admin/ManagerRegistryPage.tsx", "../src/admin/Workspace.tsx"]) {
    const text = source(page);
    assert.ok(text.includes('options: ["Individual", "LegalEntity"], optionLabels: personKindLabels'), page);
    assert.ok(text.includes('{ key: "kind", label: "Vrsta", labels: personKindLabels }'), page);
  }
  const manager = source("../src/admin/ManagerRegistryPage.tsx");
  assert.equal(manager.split('options: ["true", "false"], optionLabels: activeFilterLabels').length - 1, 4);
  assert.ok(!manager.includes('options: ["true", "false"] }'));
  assert.ok(manager.includes("<td>{formatDate(item.validFrom)}</td>"));
  const ownership = source("../src/admin/OwnershipPage.tsx");
  assert.ok(ownership.includes("<td>{label(occupantKindLabels, item.kind)}</td>"));
  assert.ok(ownership.includes("<option key={code} value={code}>{name}</option>"));
  assert.ok(!ownership.includes("<td>{item.kind}</td>") && !ownership.includes("<td>{item.validFrom}</td>"));
});
