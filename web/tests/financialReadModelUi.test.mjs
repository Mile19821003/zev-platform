import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { formatMoney } from "../src/currency.ts";
import { invoiceListView } from "../src/billingUi.ts";

test("central money formatter separates BAM EUR and unknown legacy currency", () => {
  assert.equal(formatMoney(100, "BAM"), "100,00 KM");
  assert.equal(formatMoney(50, "EUR"), "50,00 EUR");
  assert.equal(formatMoney(100, "UNKNOWN"), "100,00 [valuta nepoznata]");
});

test("invoice list keeps current paid open and currency separate", () => {
  const invoice = invoiceListView({ invoiceNumber: "INV-1", personName: "Owner",
    total: 100, paid: 60, open: 30, currencyCode: "BAM",
    paymentStatus: "PartiallyPaid", documentStatus: "Corrected" }, "2026-08");
  assert.equal(invoice.total, "100,00 KM");
  assert.equal(invoice.paid, "60,00 KM");
  assert.equal(invoice.open, "30,00 KM");
  assert.match(invoice.status, /KORIGOVAN/);
  const legacy = invoiceListView({ invoiceNumber: null, personName: "Owner",
    total: 100, paid: 0, open: 100, currencyCode: "UNKNOWN",
    paymentStatus: "Unpaid", documentStatus: "Legacy" }, "2026-08");
  assert.match(legacy.number, /LEGACY OBRAČUN/);
  assert.equal(legacy.open, "100,00 [valuta nepoznata]");
});

test("unit ledger and global report render grouped balances instead of one mixed scalar", () => {
  const finance = readFileSync(new URL("../src/admin/FinancePage.tsx", import.meta.url), "utf8");
  const reports = readFileSync(new URL("../src/admin/ReportsPage.tsx", import.meta.url), "utf8");
  assert.match(finance, /ledger\.balancesByCurrency\.map/);
  assert.match(finance, /formatMoney\(balance\.open, balance\.currencyCode\)/);
  assert.match(finance, /formatMoney\(item\.balance, item\.currencyCode\)/);
  assert.match(finance, /<Pager page=\{ledger\.page\}/);
  assert.match(reports, /summary\.balancesByCurrency\.map/);
  assert.match(reports, /formatMoney\(balance\.netCash, balance\.currencyCode\)/);
  assert.match(reports, /summary\?\.mixedCurrency/);
});

test("invoice detail and owner portal use explicit current-state currency", () => {
  const finance = readFileSync(new URL("../src/admin/FinancePage.tsx", import.meta.url), "utf8");
  const owner = readFileSync(new URL("../src/admin/OwnerFinancePage.tsx", import.meta.url), "utf8");
  const documents = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(finance, /invoiceDetail\.effectiveTotal \?\? invoiceDetail\.currentTotal/);
  assert.match(finance, /formatMoney\(invoiceDetail\.totalOpen, invoiceDetail\.currency\)/);
  assert.match(owner, /card\.balances\.map/);
  assert.match(documents, /currencyCode = "UNKNOWN"/);
});
