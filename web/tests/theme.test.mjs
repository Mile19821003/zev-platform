import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { applyTheme, colorFields, contrastRatio, contrastWarnings, factoryColors, isHexColor, matchingPreset, mixColors,
  normalizeTheme, previewStyle, themeVariables } from "../src/theme.ts";
import { iconCatalog } from "../src/navigationModel.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const backend = source("../../src/Zev.Api/Domain/SystemThemeSettings.cs");
const camel = (name) => name[0].toLowerCase() + name.slice(1);
const cssFiles = ["styles.css", "admin.css", "finance.css", "hodogram.css", "permissions.css"].map((file) => source(`../src/${file}`));

// Backend presets as colour maps (order of SystemThemeSettings.ColorNames = order of colorFields).
const backendPresets = [...backend.matchAll(/new\("(\w+)", "([^"]+)",\s*\[([^\]]+)\]\)/g)].map((match) => ({
  name: match[1], label: match[2],
  colors: Object.fromEntries([...match[3].matchAll(/"(#[0-9a-f]{6})"/g)].map((color, index) => [colorFields[index].key, color[1]])),
}));

test("factory palette equals the backend defaults and the :root block", () => {
  const defaults = Object.fromEntries([...backend.matchAll(/public string (\w+Color) \{ get; set; \} = "(#[0-9a-f]{6})";/g)]
    .map((match) => [camel(match[1]), match[2]]));
  assert.deepEqual(defaults, factoryColors);
  const root = cssFiles[0].match(/:root\{(--zev-[^}]+)\}/)[1];
  const declared = Object.fromEntries(root.split(";").map((item) => item.split(":")));
  for (const field of colorFields) assert.equal(declared[field.cssVar], factoryColors[field.key], field.cssVar);
});

test("stylesheets use the variables: no core colour is hard-coded and every variable is defined or has a fallback", () => {
  const all = cssFiles.join("\n");
  const outsideRoot = all.replace(/:root\{--zev-[^}]+\}/, "");
  for (const field of colorFields.filter((item) => !["#ffffff"].includes(factoryColors[item.key]))) {
    const plain = new RegExp(`(?<!var\\(--zev-[a-z-]+,)${factoryColors[field.key]}(?![0-9a-f])`, "i");
    assert.doesNotMatch(outsideRoot, plain, field.key);
  }
  assert.doesNotMatch(outsideRoot, /background:(white|#fff)\b/);
  const defined = new Set(colorFields.map((field) => field.cssVar));
  for (const match of all.matchAll(/var\((--zev-[a-z-]+)(,[^)]+)?\)/g))
    assert.ok(defined.has(match[1]) || match[2], `${match[1]} needs a :root value or a fallback`);
  assert.ok(all.match(/var\(--zev-primary\)/g).length >= 25);
});

test("factory palette keeps the stylesheet shades; any other palette derives them", () => {
  const factory = themeVariables(factoryColors);
  assert.equal(factory["--zev-primary"], "#176d50");
  assert.equal(factory["--zev-muted"], null);
  const dark = backendPresets.find((preset) => preset.name === "DarkNavy").colors;
  const variables = themeVariables(dark);
  assert.equal(variables["--zev-bg"], "#0f172a");
  assert.match(variables["--zev-muted"], /^#[0-9a-f]{6}$/);
  assert.equal(variables["--zev-primary-strong"], mixColors(dark.primaryColor, "#000000", 0.2));

  const style = new Map();
  const target = { setProperty: (name, value) => style.set(name, value), removeProperty: (name) => style.delete(name) };
  applyTheme(target, dark);
  assert.equal(style.get("--zev-menu-bg"), "#020617");
  assert.ok(style.has("--zev-menu-hover"));
  applyTheme(target, factoryColors);
  assert.equal(style.get("--zev-menu-bg"), "#102e31");
  assert.equal(style.has("--zev-menu-hover"), false, "shades removed again for the factory palette");
  assert.equal(previewStyle(factoryColors)["--zev-muted"], undefined);
});

test("presets come from the API, match the backend and are readable", () => {
  assert.deepEqual(backendPresets.map((preset) => preset.label), ["Tamna / Tamnoplava", "Visoki kontrast"]);
  for (const preset of backendPresets) assert.deepEqual(contrastWarnings(preset.colors), [], preset.name);
  assert.deepEqual(contrastWarnings(factoryColors), []);
  const presets = [{ name: "Default", label: "Default ERP", colors: factoryColors }, ...backendPresets];
  assert.equal(matchingPreset(factoryColors, presets), "Default");
  assert.equal(matchingPreset({ ...factoryColors, primaryColor: "#aa3300" }, presets), "Custom");
});

test("colour helpers", () => {
  assert.equal(mixColors("#000000", "#ffffff", 0.5), "#808080");
  assert.equal(contrastRatio("#000000", "#ffffff").toFixed(1), "21.0");
  assert.equal(contrastRatio("#176d50", "#176d50"), 1);
  assert.ok(isHexColor("#A1b2C3"));
  for (const invalid of ["#12345", "red", "#1234567", "url(x)", null]) assert.equal(isHexColor(invalid), false);
  const warnings = contrastWarnings({ ...factoryColors, textColor: "#eeeeee" });
  assert.ok(warnings.some((warning) => warning.startsWith("Tekst na pozadini")));
});

test("API answer is normalized; invalid colours fall back to the factory value", () => {
  const theme = normalizeTheme({ activeThemeName: "Custom", primaryColor: "#AA3300", textColor: "javascript:x",
    updatedAt: "2026-10-11T00:00:00Z", presets: [{ name: "Default", label: "Default ERP", colors: { primaryColor: "#176d50" } }] });
  assert.equal(theme.colors.primaryColor, "#aa3300");
  assert.equal(theme.colors.textColor, factoryColors.textColor);
  assert.equal(theme.presets[0].colors.menuBgColor, factoryColors.menuBgColor);
  assert.equal(normalizeTheme(null).activeThemeName, "Default");
});

test("theme screen: picker per colour, actions and module wiring", () => {
  const page = source("../src/admin/ThemeSettingsPage.tsx");
  assert.match(page, /type="color"/);
  assert.match(page, /colorFields\.map/);
  for (const label of ["Snimi boje", "Odbaci izmjene", "Vrati na tvorničke postavke"]) assert.ok(page.includes(label), label);
  assert.match(page, /previewTheme\(/);
  assert.equal(colorFields.length, 12);
  const entry = iconCatalog().find((item) => item.moduleKey === "themes");
  assert.equal(entry?.category, "Administracija i sistem");
  assert.match(source("../src/admin/Workspace.tsx"), /active === "themes" && <ThemeSettingsPage/);
  assert.match(source("../src/main.tsx"), /applyCachedTheme\(\)/);
});
