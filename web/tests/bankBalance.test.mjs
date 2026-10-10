import assert from "node:assert/strict";
import test from "node:test";
import { balanceStatus, bankBalanceLine, formatDate, gapLines, reconciliationRows } from "../src/bankBalanceUi.ts";

// Shape of GET /payments/accounts/{id}/balance from the API test scenario (two CAMT statements with a gap,
// a CSV account of the same fund, a manual payment and a posted expense).
const balance = {
  accountId: "a", accountNumber: "1610000012345678", bankName: null, currencyCode: "BAM", isActive: true,
  bank: { balanceSource: "Statement", reportedBalance: { amount: 145, asOf: "2026-10-31", importId: "i2" },
    derivedBalance: 140, derivedMatchesReported: false, credits: 50, debits: -10, movement: 40, statementCount: 2,
    gaps: [{ previousImportId: "i1", nextImportId: "i2", previousClosing: 120, nextOpening: 125, difference: 5 }] },
  fund: { fundId: "f", fundName: "Redovni", currencyCode: "BAM", balance: 75, fromThisAccount: 50, fromOtherAccounts: 15,
    manualPayments: 50, expenses: -40, other: 0, otherCurrencyEntries: 0 },
  reconciliation: { bankMovement: 40, fundBalance: 75, difference: -35, bankOutflowsNotInFund: -10,
    manualPaymentsNotInBank: 50, expensesNotInBank: -40, otherBankAccountsOfFund: 15, otherFundEntries: 0, unexplained: 0 },
};

test("reported bank balance shows amount and statement date; missing balance is said plainly", () => {
  assert.equal(bankBalanceLine(balance), "145,00 KM na dan 31.10.2026");
  assert.equal(bankBalanceLine({ ...balance, bank: { ...balance.bank, balanceSource: "NotProvided", reportedBalance: null } }),
    "Izvodi ne sadrže saldo (CSV/TXT) — prikazan je samo promet.");
  assert.equal(formatDate("2026-10-31"), "31.10.2026");
});

test("reconciliation rows add up to the difference and skip zero parts", () => {
  const rows = reconciliationRows(balance);
  assert.deepEqual(rows.map((row) => row.label), [
    "Bankovni odlivi koji nisu u knjizi fonda (provizije, isplate)", "Ručne uplate u fondu bez bankovnog izvoda",
    "Rashodi knjiženi u fondu", "Uplate na drugim računima istog fonda"]);
  assert.deepEqual(rows.map((row) => row.amount), ["-10,00 KM", "-50,00 KM", "40,00 KM", "-15,00 KM"]);
  assert.equal(-10 - 50 + 40 - 15, balance.reconciliation.difference);
});

test("status: gap or unexplained difference warns, explained difference informs, equal is ok", () => {
  assert.deepEqual(balanceStatus(balance), { tone: "warning", label: "Prekid u nizu izvoda" });
  const noGap = { ...balance, bank: { ...balance.bank, gaps: [] } };
  assert.deepEqual(balanceStatus(noGap), { tone: "info", label: "Razlika je objašnjena" });
  assert.equal(balanceStatus({ ...noGap, reconciliation: { ...noGap.reconciliation, unexplained: 2.5 } }).label,
    "Neobjašnjena razlika 2,50 KM");
  assert.equal(balanceStatus({ ...noGap, reconciliation: { ...noGap.reconciliation, difference: 0 } }).tone, "ok");
  assert.match(gapLines(balance)[0], /Završni saldo 120,00 KM, sljedeći izvod počinje s 125,00 KM \(razlika 5,00 KM\)/);
});
