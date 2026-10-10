import assert from "node:assert/strict";
import test from "node:test";
import { cellState, cellView, nextEffect, pendingChanges, visibleColumns } from "../src/permissionMatrixUi.ts";

const inherited = { key: "Payments.View", roleDefault: true, override: null, effective: true };
const notAllowed = { key: "Payments.ChangePayer", roleDefault: false, override: null, effective: false };
const granted = { key: "Access.Manage", roleDefault: false, override: "Grant", effective: true };
const grantedOverDefault = { key: "Reports.View", roleDefault: true, override: "Grant", effective: true };
const denied = { key: "Payments.Export", roleDefault: true, override: "Deny", effective: false };

test("inherited ✓ and manual GRANT ✓+ look different", () => {
  const view = (base) => cellView(cellState(base, {}, base.key), "Manager");
  assert.deepEqual(view(inherited), { glyph: "✓", className: "inherited", label: "Naslijeđeno iz uloge Manager" });
  assert.deepEqual(view(granted), { glyph: "✓+", className: "granted", label: "Ručno dozvoljeno" });
  assert.equal(view(grantedOverDefault).label, "Ručno dozvoljeno (uloga Manager već dozvoljava)");
  assert.deepEqual(view(denied), { glyph: "✕", className: "denied", label: "Ručno zabranjeno (uloga Manager bi dozvolila)" });
  assert.deepEqual(view(notAllowed), { glyph: "□", className: "none", label: "Nije dozvoljeno ulogom Manager" });
});

test("unsaved draft is applied over the role default and marked as changed", () => {
  const draftDeny = cellState(inherited, { "Payments.View": "Deny" }, "Payments.View");
  assert.deepEqual(draftDeny, { effect: "Deny", inherited: true, effective: false, changed: true });
  assert.equal(cellView(draftDeny, "Manager").className, "denied changed");
  assert.match(cellView(draftDeny, "Manager").label, /nesačuvano/);
  const reset = cellState(denied, { "Payments.Export": null }, "Payments.Export");
  assert.deepEqual(reset, { effect: null, inherited: true, effective: true, changed: true });
  assert.equal(cellView(reset, "Manager").glyph, "✓");
  // Resetting a key that has no override is not a change.
  assert.equal(cellState(inherited, { "Payments.View": null }, "Payments.View").changed, false);
});

test("click cycles default → opposite override → default", () => {
  assert.equal(nextEffect(cellState(inherited, {}, inherited.key)), "Deny");
  assert.equal(nextEffect(cellState(notAllowed, {}, notAllowed.key)), "Grant");
  assert.equal(nextEffect(cellState(denied, {}, denied.key)), null);
  assert.equal(nextEffect(cellState(granted, {}, granted.key)), null);
});

test("only real changes are sent when saving", () => {
  const permissions = [inherited, notAllowed, granted, denied];
  const draft = { "Payments.View": null, "Payments.ChangePayer": "Grant", "Access.Manage": null,
    "Payments.Export": "Deny" };
  assert.deepEqual(pendingChanges(permissions, draft), [
    { key: "Payments.ChangePayer", effect: "Grant" }, { key: "Access.Manage", effect: null }]);
  // "Vrati sve na default uloge" drafts only keys with an override.
  const resetAll = Object.fromEntries(permissions.filter((item) => item.override !== null).map((item) => [item.key, null]));
  assert.deepEqual(pendingChanges(permissions, resetAll).map((item) => item.key), ["Access.Manage", "Payments.Export"]);
});

test("columns without any module action (Delete) are hidden", () => {
  const modules = [
    { name: "Finance", label: "Finansije", actions: ["View", "Create", "Edit", "Approve", "Export"], details: [] },
    { name: "Payments", label: "Uplate", actions: ["View", "Export"], details: ["Payments.ReconcileManual"] },
  ];
  assert.deepEqual(visibleColumns(modules), ["View", "Create", "Edit", "Approve", "Export"]);
  assert.deepEqual(visibleColumns([...modules, { name: "Docs", label: "Dokumenti", actions: ["Delete"], details: [] }]),
    ["View", "Create", "Edit", "Delete", "Approve", "Export"]);
  assert.deepEqual(visibleColumns([]), []);
});
