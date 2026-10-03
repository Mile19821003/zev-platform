import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { activeAllocationCount, allocationStatusLabel, bankTransactionLabel, canSubmitPayerCorrection,
  matchRuleLabel, paymentLabel } from "../src/paymentUi.ts";
import { formatMoney, ownerLedgerLabel } from "../src/currency.ts";

test("payment labels distinguish unrecognized review partial credit allocated and returned", () => {
  const base = { status: "Posted", personId: "person", bankStatus: "Matched", allocated: 0, amount: 100 };
  assert.equal(paymentLabel({ ...base, bankStatus: "ManualReview" }), "ZA POTVRDU");
  assert.equal(paymentLabel({ ...base, personId: null, bankStatus: "ManualReview" }), "NEPREPOZNATO");
  assert.equal(paymentLabel({ ...base, personId: null }), "NEPREPOZNATO");
  assert.equal(paymentLabel({ ...base, allocated: 40 }), "DJELIMIČNO RASPOREĐENO");
  assert.equal(paymentLabel(base), "KREDIT / PREPLATA");
  assert.equal(paymentLabel({ ...base, allocated: 100 }), "RASPOREĐENO");
  assert.equal(paymentLabel({ ...base, status: "Reversed" }), "STORNIRANO / VRAĆENO");
});

test("match explanation names deterministic rules without a confidence score", () => {
  assert.equal(matchRuleLabel("EXACT_REFERENCE_V1"), "Tačan poziv na broj");
  assert.equal(matchRuleLabel("ACCOUNT_REFERENCE_OLDEST_DUE_V1"), "Stabilna referenca jedinice");
  assert.match(matchRuleLabel(null), /bez automatskog matcha/);
});

test("payment workspace wires manual payment credit preview and allocation correction", () => {
  const source = readFileSync(new URL("../src/admin/PaymentWorkspace.tsx", import.meta.url), "utf8");
  assert.match(source, /NOVA RUČNA UPLATA/);
  assert.match(source, /SAČUVAJ KAO KREDIT/);
  assert.match(source, /SAČUVAJ I RASPOREDI/);
  assert.match(source, /automatic-preview/);
  assert.match(source, /ISPRAVI RASPOREĐIVANJE/);
  assert.match(source, /open-charges/);
});

test("bank transactions have a separate paginated workspace", () => {
  const source = readFileSync(new URL("../src/admin/BankTransactionsPage.tsx", import.meta.url), "utf8");
  assert.match(source, /bank-transactions\?/);
  assert.match(source, /<Pager/);
  assert.match(source, /Otvori sravnjenje/);
});

test("payer correction requires reason and warning acknowledgement for active allocations", () => {
  const rows = [{ id: "original", amount: 100, reversesAllocationId: null }];
  assert.equal(activeAllocationCount(rows), 1);
  assert.equal(canSubmitPayerCorrection("A", "B", "error", 1, false), false);
  assert.equal(canSubmitPayerCorrection("A", "B", " ", 1, true), false);
  assert.equal(canSubmitPayerCorrection("A", "A", "error", 1, true), false);
  assert.equal(canSubmitPayerCorrection("A", "B", "error", 1, true), true);
  assert.equal(activeAllocationCount([...rows,
    { id: "reversal", amount: -100, reversesAllocationId: "original" }]), 0);
  const source = readFileSync(new URL("../src/admin/PaymentWorkspace.tsx", import.meta.url), "utf8");
  assert.match(source, /change-payer/);
  assert.match(source, /postojeće raspodjele moraju biti poništene/);
});

test("currency and derived bank status never label partial credit as fully allocated", () => {
  assert.match(formatMoney(100, "BAM"), /KM/);
  assert.match(formatMoney(100, "EUR"), /EUR/);
  assert.doesNotMatch(formatMoney(100, "EUR"), /KM/);
  assert.equal(bankTransactionLabel("Matched", 120, 100, "Owner"),
    "DJELIMIČNO RASPOREĐENO / KREDIT OSTAJE");
  assert.equal(bankTransactionLabel("Matched", 100, 0, "Owner"), "IDENTIFIKOVANO, NERASPOREĐENO");
  assert.equal(bankTransactionLabel("Matched", 100, 100, "Owner"), "POTPUNO RASPOREĐENO");
  assert.equal(allocationStatusLabel("PARTIALLY_ALLOCATED_CREDIT_REMAINS"),
    "DJELIMIČNO RASPOREĐENO / KREDIT OSTAJE");
});

test("owner card shows currency groups and server page metadata", () => {
  const source = readFileSync(new URL("../src/admin/FinancePage.tsx", import.meta.url), "utf8");
  assert.match(source, /ownerCard\.balances\.map/);
  assert.match(source, /ownerCard\.totalCount/);
  assert.match(source, /ownerCardPage/);
  assert.match(source, /parameters\.set\("to", ownerCardTo\)/);
});

test("owner portal uses grouped currency balances and paginated event ledger", () => {
  const source = readFileSync(new URL("../src/admin/OwnerFinancePage.tsx", import.meta.url), "utf8");
  assert.match(source, /card\.balances\.map/);
  assert.match(source, /balance\.credit/);
  assert.match(source, /balance\.netBalance/);
  assert.match(source, /card\.events\.map/);
  assert.match(source, /event\.runningBalance/);
  assert.match(source, /card\.totalCount/);
  assert.match(source, /pageSize=20/);
  assert.doesNotMatch(source, /toFixed\(2\).*KM/);
  assert.match(source, /invoice\.documentStatus === "Corrected"/);
  assert.match(source, /invoice\.documentStatus === "Cancelled"/);
  assert.match(formatMoney(100, "BAM"), /KM/);
  assert.match(formatMoney(50, "EUR"), /EUR/);
  assert.doesNotMatch(formatMoney(50, "EUR"), /KM/);
  assert.equal(ownerLedgerLabel("Correction"), "Korekcija");
  assert.equal(ownerLedgerLabel("Storno"), "Storno");
  assert.equal(ownerLedgerLabel("AllocationRelease"), "Ispravka raspoređivanja");
});
