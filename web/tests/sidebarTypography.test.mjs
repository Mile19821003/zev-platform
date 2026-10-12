import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync(new URL("../src/admin.css", import.meta.url), "utf8");
const rule = (selector) => {
  const start = css.lastIndexOf(`${selector}{`);
  assert.ok(start >= 0, `missing rule ${selector}`);
  return css.slice(start, css.indexOf("}", start));
};

test("sidebar: compact medium-weight items under small uppercase group labels", () => {
  const item = rule(".workspace-nav .nav-item");
  assert.match(item, /font-size:\.92rem/);
  assert.match(item, /font-weight:500/);
  assert.match(item, /color:color-mix\(in srgb,var\(--zev-menu-text\) 82%,var\(--zev-menu-bg\)\)/);
  assert.match(rule(".workspace-nav .nav-item.active"), /font-weight:600/);
  const header = rule(".workspace-nav .nav-group-header");
  assert.match(header, /font-size:\.78rem/);
  assert.match(header, /text-transform:uppercase/);
  // Collapsed (icon-only) menu keeps the full-size icons.
  assert.ok(css.includes(".workspace-nav:not(.collapsed) .nav-group-items .nav-icon{width:17px;height:17px}"));
});
