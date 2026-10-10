import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { iconPalette, isIconKey, normalizeModuleIcons, paletteIcon, searchIcons } from "../src/iconRegistry.ts";
import { categoryKey, defaultIcon, featuredModules, groupNavigation, iconCatalog, iconFor, navGroups, setModuleIcons }
  from "../src/navigationModel.ts";

// Backend palette and default icons (src/Zev.Api/Domain/ModuleIconSetting.cs).
const backend = readFileSync(new URL("../../src/Zev.Api/Domain/ModuleIconSetting.cs", import.meta.url), "utf8");
const backendKeys = [...backend.slice(backend.indexOf("IconKeys ="), backend.indexOf("];", backend.indexOf("IconKeys =")))
  .matchAll(/"([a-z0-9-]+)"/g)].map((match) => match[1]);
const backendDefaults = Object.fromEntries([...backend.slice(backend.indexOf("Defaults ="))
  .matchAll(/\("([^"]+)", "([^"]+)"\)/g)].map((match) => [match[1], match[2]]));

test("palette has 35–40 unique icons and matches the backend validation list", () => {
  assert.ok(iconPalette.length >= 35 && iconPalette.length <= 40, String(iconPalette.length));
  assert.equal(new Set(iconPalette.map((icon) => icon.key)).size, iconPalette.length);
  assert.deepEqual(iconPalette.map((icon) => icon.key), backendKeys);
  for (const icon of iconPalette) assert.ok(icon.label && icon.tags.length > 0 && /^M/.test(icon.path), icon.key);
});

test("every module and category has a default icon equal to the backend seed", () => {
  const catalog = iconCatalog();
  assert.equal(catalog.length, Object.keys(backendDefaults).length);
  for (const entry of catalog) {
    assert.equal(backendDefaults[entry.moduleKey], entry.defaultIcon, entry.moduleKey);
    assert.ok(isIconKey(entry.defaultIcon), entry.moduleKey);
  }
  assert.equal(new Set(catalog.map((entry) => entry.moduleKey)).size, catalog.length);
  assert.deepEqual(catalog.filter((entry) => entry.moduleKey.startsWith("category:")).map((entry) => entry.category),
    navGroups.map((group) => group.label));
});

test("icon picker search uses label, key and tags without diacritics", () => {
  assert.ok(searchIcons("sifarnik").some((icon) => icon.key === "barcode"));
  assert.ok(searchIcons("NOVAC").some((icon) => icon.key === "banknote"));
  assert.deepEqual(searchIcons("chart").map((icon) => icon.key), ["chart-bar", "chart-pie"]);
  assert.equal(searchIcons("").length, iconPalette.length);
  assert.deepEqual(searchIcons("nema-takve"), []);
  assert.equal(paletteIcon("nepostojeca").key, "list");
});

test("navigation and dashboard render the configured icons, falling back to defaults", () => {
  const menu = [{ key: "overview", label: "Pregled" }, { key: "payments", label: "Uplate i izvodi" },
    { key: "lookups", label: "Šifarnici" }];
  const overrides = { payments: "credit-card", [categoryKey("finance")]: "chart-pie", lookups: "list" };
  const groups = groupNavigation(menu, overrides);
  assert.equal(groups.find((group) => group.id === "finance").icon, "chart-pie");
  assert.equal(groups.find((group) => group.id === "finance").items[0].icon, "credit-card");
  assert.equal(groups.find((group) => group.id === "admin").icon, "shield-check");
  assert.equal(iconFor("overview", {}), "home");
  assert.equal(defaultIcon("category:registry"), "building-2");
  try {
    // The app sets the map once per render; featured cards and the sidebar read it.
    setModuleIcons(overrides);
    assert.deepEqual(featuredModules(menu), [{ key: "lookups", label: "Šifarnici", icon: "list" }]);
    assert.equal(groupNavigation(menu)[0].items[0].icon, "credit-card");
  } finally { setModuleIcons({}); }
});

test("the API response is accepted only with palette keys", () => {
  assert.deepEqual(normalizeModuleIcons({ icons: { payments: "credit-card", reports: "rocket", x: 3 } }), { payments: "credit-card" });
  assert.deepEqual(normalizeModuleIcons(null), {});
  assert.deepEqual(normalizeModuleIcons({ icons: "pogrešno" }), {});
});
