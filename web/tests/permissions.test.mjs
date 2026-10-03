import assert from "node:assert/strict";
import test from "node:test";
import { canShowPaymentAction, hasStaffPermission } from "../src/permissionUi.ts";

test("Manager payment UAT keeps menu, account list, preview and export but hides writes", () => {
  const permissions = ["Payments.View", "BankAccounts.View", "BankStatements.View",
    "BankStatements.Preview", "Payments.Export"];
  const zev = { isStaff: true, permissions };
  assert.equal(hasStaffPermission(zev, "Payments.View"), true);
  assert.equal(canShowPaymentAction(permissions, "BankAccounts.View"), true);
  assert.equal(canShowPaymentAction(permissions, "BankStatements.Preview"), true);
  assert.equal(canShowPaymentAction(permissions, "Payments.Export"), true);
  for (const key of ["BankAccounts.Create", "BankStatements.Import", "BankStatements.Post",
    "Payments.ReconcileManual", "Payments.Reverse"])
    assert.equal(canShowPaymentAction(permissions, key), false, key);
  assert.equal(hasStaffPermission({ isStaff: true, permissions: [] }, "Payments.View"), false);
  assert.equal(hasStaffPermission({ isStaff: false, permissions }, "Payments.View"), false);
});
