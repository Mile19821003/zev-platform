import assert from "node:assert/strict";
import test from "node:test";
import { billingDraftSignature, billingIssueText, billingItemTitle, defaultBillingSelection,
  invoiceListView, manualLineAmount, similarBillingWarnings } from "../src/billingUi.ts";
import { publishChange, subscribeChanges } from "../src/dataSync.ts";
import { ApiError, createApiClient } from "../src/apiClient.ts";

const ordinary = { id: "one", code: "OD1", version: 1, name: "Redovno održavanje",
  basis: "SquareMeter", rate: .5, fundId: "fund", fundName: "Redovno održavanje",
  validFrom: "2026-10-01", validTo: null, isActive: true, isOptional: false, showOnInvoice: true };
const uat = { ...ordinary, id: "two", code: "UAT-OD1", isOptional: true };

test("same-name items retain distinct codes in billing UI model", () => {
  assert.equal(billingItemTitle(ordinary), "OD1 | Redovno održavanje");
  assert.equal(billingItemTitle(uat), "UAT-OD1 | Redovno održavanje");
});

test("similar selected codes warn without blocking selection", () => {
  assert.deepEqual(similarBillingWarnings([ordinary, uat], [ordinary.id, uat.id]),
    ["Odabrane su dvije slične stavke: OD1 i UAT-OD1. Provjerite da li obje trebaju biti uključene u obračun."]);
  assert.deepEqual(similarBillingWarnings([ordinary, uat], [ordinary.id]), []);
});

test("mandatory items default on, optional items default off", () => {
  assert.deepEqual(defaultBillingSelection([ordinary, uat]), [ordinary.id]);
});

test("preflight issue uses localized code and unit, not English message", () => {
  assert.equal(billingIssueText({ code: "PROPERTY_UNIT_NO_ACTIVE_OWNER", propertyUnitCode: "A-02",
    message: "Property unit A-02 has no active owner for selected period." }, "2026-10"),
  "Posebni dio A-02 nema aktivnog vlasnika za 10/2026.");
  assert.match(billingIssueText({ code: "FUND_BANK_ACCOUNT_MISSING", fundName: "Redovno održavanje",
    message: "Fund requires an account." }, "2026-10"), /Redovno održavanje.*nema aktivan bankovni račun/);
});

test("period, due date, selection and override invalidate draft signature", () => {
  const original = billingDraftSignature("zev", "2026-10", "2026-10-30", [ordinary.id], []);
  assert.notEqual(original, billingDraftSignature("zev", "2026-11", "2026-10-30", [ordinary.id], []));
  assert.notEqual(original, billingDraftSignature("zev", "2026-10", "2026-10-31", [ordinary.id], []));
  assert.notEqual(original, billingDraftSignature("zev", "2026-10", "2026-10-30", [ordinary.id, uat.id], []));
  assert.notEqual(original, billingDraftSignature("zev", "2026-10", "2026-10-30", [ordinary.id],
    [{ propertyUnitId: "unit", tariffVersionId: ordinary.id, include: true,
      manualAmount: 2, note: null, reason: "UAT" }]));
});

test("ownership data-sync notifies finance subscriber without hard refresh", () => {
  let revisions = 0;
  const unsubscribe = subscribeChanges(["ownerships"], () => { revisions++; });
  try { publishChange("/api/system/ownerships"); }
  finally { unsubscribe(); }
  assert.equal(revisions, 1);
});

test("read-only preflight preserves finance revision and preview errors retain issue codes", async () => {
  let revisions = 0;
  const unsubscribe = subscribeChanges(["zev:zev:finance"], () => { revisions++; });
  try {
    const preflight = createApiClient("http://local", async () => Response.json({ canPreview: true, issues: [] }));
    await preflight("/api/zevs/zev/finance/billing-runs/preflight", { method: "POST" });
    assert.equal(revisions, 0);
    const preview = createApiClient("http://local", async () => Response.json({
      detail: "Property unit A-02 has no active owner for selected period.",
      issues: [{ code: "PROPERTY_UNIT_NO_ACTIVE_OWNER", propertyUnitCode: "A-02" }],
    }, { status: 400 }));
    await assert.rejects(preview("/api/zevs/zev/finance/billing-runs/preview", { method: "POST" }), (error) => {
      assert.ok(error instanceof ApiError);
      assert.equal(error.issues[0].code, "PROPERTY_UNIT_NO_ACTIVE_OWNER");
      return true;
    });
  } finally { unsubscribe(); }
});

test("posting a preview publishes finance refresh for invoice list", async () => {
  let revisions = 0;
  const unsubscribe = subscribeChanges(["zev:zev:finance"], () => { revisions++; });
  try {
    const api = createApiClient("http://local", async () => Response.json({ alreadyExists: false }));
    await api("/api/zevs/zev/finance/billing-runs", { method: "POST",
      body: JSON.stringify({ period: "2026-10-01", draft: { expectedHash: "test" } }) });
    assert.equal(revisions, 1);
  } finally { unsubscribe(); }
});

test("free manual line preview applies discount and invalidates draft signature", () => {
  const line = { id: "manual-1", propertyUnitId: "unit", fundId: "fund",
    description: "Poseban rad", unitOfMeasure: "sat", quantity: 2, unitPrice: 10,
    discountType: "Fixed", discountValue: 3, note: null, reason: "Odluka" };
  assert.equal(manualLineAmount(line), 17);
  assert.notEqual(billingDraftSignature("zev", "2026-10", "2026-10-30", [], [], [line]),
    billingDraftSignature("zev", "2026-10", "2026-10-30", [], [], [{ ...line, unitPrice: 11 }]));
});

test("Invoice V2 list presents number, period, owner, amount and balances", () => {
  assert.deepEqual(invoiceListView({ invoiceNumber: "INV-2026-000001", personName: "Vlasnik A",
    total: 53, paid: 10, open: 43, currencyCode: "BAM",
    paymentStatus: "PartiallyPaid", documentStatus: "Issued" }, "2026-10"),
  { number: "INV-2026-000001", period: "2026-10", owner: "Vlasnik A", total: "53,00 KM",
    paid: "10,00 KM", open: "43,00 KM", status: "PartiallyPaid · IZDAT" });
});

test("legacy billing remains explicitly visible without an invoice number", () => {
  assert.equal(invoiceListView({ invoiceNumber: null, personName: "Vlasnik A", total: 53, paid: 0, open: 53,
    paymentStatus: "Unpaid", documentStatus: "Legacy" }, "2026-10").number,
  "LEGACY OBRAČUN (informativno)");
});
