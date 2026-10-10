import assert from "node:assert/strict";
import test from "node:test";
import { isIconKey } from "../src/iconRegistry.ts";
import { groupNavigation, groupOf, loadSidebarState, moduleLabel, navGroups, saveSidebarState }
  from "../src/navigationModel.ts";

// Role workspace menu of a manager (already filtered by effective permissions in RoleWorkspace).
const manager = [
  { key: "overview", label: "Pregled" }, { key: "finance", label: "Finansije" },
  { key: "payments", label: "Uplate i izvodi" }, { key: "bank-transactions", label: "Bankovne transakcije" },
  { key: "expenses", label: "Rashodi i dugovanja" }, { key: "reports", label: "Izvještaji" },
  { key: "legal", label: "Pravni predmeti" }, { key: "registry", label: "Vlasnici i nekretnine" },
  { key: "maintenance", label: "Održavanje" }, { key: "community", label: "Zajednica" },
  { key: "access", label: "Uloge i pristupi" }, { key: "hodogram", label: "Hodogram" },
];

test("modules are grouped into the four categories in a fixed order", () => {
  const groups = groupNavigation(manager);
  assert.deepEqual(groups.map((group) => group.label),
    ["Finansije i računi", "Registar i nekretnine", "Zajednica i održavanje", "Administracija i sistem"]);
  assert.deepEqual(groups[0].items.map((item) => item.label),
    ["Obračun i računi", "Uplate i izvodi", "Bankovne transakcije", "Rashodi i dugovanja", "Izvještaji", "Pravni predmeti"]);
  assert.deepEqual(groups[3].items.map((item) => item.label), ["Uloge i matrica prava", "Hodogram"]);
  assert.ok(groups.every((group) => group.items.every((item) => item.key !== "overview")));
});

test("categories without a permitted module are not shown", () => {
  // Owner: own finances, own tickets, documents and voting only.
  const owner = [{ key: "overview", label: "Pregled" }, { key: "owner-finance", label: "Moj portal" },
    { key: "my-maintenance", label: "Moje prijave" }, { key: "owner-community", label: "Dokumenti i glasanje" }];
  assert.deepEqual(groupNavigation(owner).map((group) => group.id), ["finance", "community"]);
  assert.deepEqual(groupNavigation([{ key: "overview", label: "Pregled" }]), []);
});

test("system administrator menu maps every item; unknown keys fall back to administration", () => {
  const system = ["organizations", "zevs", "buildings", "entrances", "units", "people", "ownership", "users",
    "memberships", "accountLinks", "finance", "payments", "expenses", "legal", "maintenance", "hodogram",
    "community", "reports"].map((key) => ({ key, label: key }));
  const groups = groupNavigation(system);
  assert.equal(groups.flatMap((group) => group.items).length, system.length);
  assert.deepEqual(groups.find((group) => group.id === "registry").items.map((item) => item.key),
    ["zevs", "buildings", "entrances", "units", "people", "ownership"]);
  assert.equal(moduleLabel({ key: "accountLinks", label: "Pristupi i veze naloga" }), "Veze naloga");
  assert.equal(groupOf("nepoznato"), "admin");
  assert.equal(groupOf("overview"), null);
  for (const group of groups) for (const item of group.items) assert.ok(isIconKey(item.icon), item.key);
  for (const group of navGroups) assert.ok(isIconKey(group.icon));
});

test("sidebar state is remembered and survives broken storage", () => {
  const values = new Map();
  const storage = { getItem: (key) => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
  assert.deepEqual(loadSidebarState(storage), { collapsed: false, expanded: [] });
  saveSidebarState(storage, { collapsed: true, expanded: ["finance", "admin"] });
  assert.deepEqual(loadSidebarState(storage), { collapsed: true, expanded: ["finance", "admin"] });
  values.set("zev.nav.expanded", "[\"finance\",\"nepoznato\",3]");
  assert.deepEqual(loadSidebarState(storage).expanded, ["finance"]);
  values.set("zev.nav.expanded", "{nije json");
  assert.deepEqual(loadSidebarState(storage), { collapsed: false, expanded: [] });
  const throwing = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); } };
  assert.deepEqual(loadSidebarState(throwing), { collapsed: false, expanded: [] });
  assert.doesNotThrow(() => saveSidebarState(throwing, { collapsed: true, expanded: [] }));
  assert.deepEqual(loadSidebarState(undefined), { collapsed: false, expanded: [] });
});
