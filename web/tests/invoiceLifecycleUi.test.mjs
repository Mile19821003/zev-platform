import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { canCorrect, canStorno, coalesceTimelineEvents, correctionSignedTotal,
  documentStatusLabel, paymentSplitLabel, primaryInvoiceStatus, timelineEventLabel, validateCorrection,
  validateCorrectionLines, validateDocumentReason } from "../src/invoiceLifecycleUi.ts";

test("storno action requires issued Invoice V2 and permission", () => {
  assert.equal(canStorno("invoice", "Issued", true), true);
  assert.equal(canStorno("invoice", "Issued", false), false);
  assert.equal(canStorno(null, "Legacy", true), false);
  assert.equal(canStorno("invoice", "Cancelled", true), false);
  assert.equal(canStorno("invoice", "Corrected", true), false);
});

test("split payment and manual reversal are distinct timeline presentations", () => {
  assert.match(paymentSplitLabel(100, 60, "BAM"), /Na ovaj račun: 60,00 KM/);
  assert.match(paymentSplitLabel(100, 40, "EUR"), /Na ovaj račun: 40,00 EUR/);
  assert.equal(timelineEventLabel("ManualAllocationReversal"), "ISPRAVKA RASPOREĐIVANJA");
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /item\.reason/);
  assert.match(source, /paymentSplitLabel/);
});

test("correction action supports issued and corrected invoices only", () => {
  assert.equal(canCorrect("invoice", "Issued", true), true);
  assert.equal(canCorrect("invoice", "Corrected", true), true);
  assert.equal(canCorrect("invoice", "Cancelled", true), false);
  assert.equal(canCorrect(null, "Legacy", true), false);
  assert.equal(canCorrect("invoice", "Issued", false), false);
});

test("reason and correction line are validated before request", () => {
  assert.match(validateDocumentReason(" "), /Razlog je obavezan/);
  assert.equal(validateDocumentReason("Pogrešno knjiženje"), null);
  assert.match(validateCorrection("Credit", "", "", "", "10"), /izvornu stavku/);
  assert.match(validateCorrection("Debit", "", "", "", "10"), /fond i opis/);
  assert.match(validateCorrection("Debit", "line", "", "", "0"), /Iznos/);
  assert.equal(validateCorrection("Debit", "", "fund", "Nova stavka", "15.00"), null);
});

test("document lifecycle labels distinguish cancelled corrected and legacy", () => {
  assert.equal(documentStatusLabel("Cancelled"), "STORNIRAN");
  assert.equal(documentStatusLabel("Corrected"), "KORIGOVAN");
  assert.equal(documentStatusLabel("Legacy"), "LEGACY OBRAČUN");
});

test("admin action wiring includes confirmation, reason, timeline and document PDFs", () => {
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /window\.confirm\(/);
  assert.match(source, /Razlog \*/);
  assert.match(source, /STORNIRAJ RAČUN/);
  assert.match(source, /KREIRAJ KOREKCIJU/);
  assert.match(source, /\/timeline/);
  assert.match(source, /\/documents\/\$\{document\.id\}\.pdf/);
});

test("owner portal wires related documents without write actions", () => {
  const source = readFileSync(new URL("../src/admin/OwnerFinancePage.tsx", import.meta.url), "utf8");
  assert.match(source, /Povezani dokumenti/);
  assert.match(source, /finance\/documents\/\$\{document\.id\}\.pdf/);
  assert.match(source, /<InvoiceDocuments zevId=\{zevId\}/);
  assert.doesNotMatch(source, /canReverse=\{true\}|canAmend=\{true\}/);
});

test("confirmation and required storno reason remain wired", () => {
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /window\.confirm\(/);
  assert.match(source, /validateDocumentReason\(reason\)/);
  assert.match(validateDocumentReason(""), /Razlog je obavezan/);
});

test("timeline combines payment allocations and releases without duplicating a payment", () => {
  const paymentId = "payment-1";
  const documentId = "storno-1";
  const entries = [
    { type: "Payment", sourceId: paymentId, amount: 60 },
    { type: "Payment", sourceId: paymentId, amount: 40 },
    { type: "AllocationRelease", sourceId: paymentId, financialDocumentId: documentId, amount: 60 },
    { type: "AllocationRelease", sourceId: paymentId, financialDocumentId: documentId, amount: 40 },
  ];
  assert.deepEqual(coalesceTimelineEvents(entries).map((item) => [item.type, item.amount]),
    [["Payment", 100], ["AllocationRelease", 100]]);
});

test("timeline labels credit, debit, storno and released credit", () => {
  assert.equal(timelineEventLabel("Credit"), "KOREKCIJA – UMANJENJE");
  assert.equal(timelineEventLabel("Debit"), "KOREKCIJA – DODATNO ZADUŽENJE");
  assert.equal(timelineEventLabel("Storno"), "STORNO");
  assert.equal(timelineEventLabel("AllocationRelease"), "OSLOBOĐEN KREDIT");
});

test("multi-line credit keeps negative total and rejects an unrelated new line", () => {
  const lines = [{ originalInvoiceLineId: "maintenance", fundId: null, description: null, amount: 10 },
    { originalInvoiceLineId: "garage", fundId: null, description: null, amount: 5 }];
  assert.equal(validateCorrectionLines("Credit", lines), null);
  assert.equal(correctionSignedTotal("Credit", lines), -15);
  assert.match(validateCorrectionLines("Credit", [...lines, { originalInvoiceLineId: null,
    fundId: "fund", description: "Other", amount: 1 }]), /izvornu stavku/);
});

test("multi-line debit and new item keep positive total", () => {
  const lines = [{ originalInvoiceLineId: "maintenance", fundId: null, description: null, amount: 7 },
    { originalInvoiceLineId: null, fundId: "regular", description: "New", amount: 3 }];
  assert.equal(validateCorrectionLines("Debit", lines), null);
  assert.equal(correctionSignedTotal("Debit", lines), 10);
  assert.equal(validateCorrectionLines("Debit", [{ ...lines[0], amount: 0.29 }]), null);
  assert.match(validateCorrectionLines("Debit", lines.map((item) => ({ ...item, amount: -1 }))), /pozitivan/);
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /UKUPNA KOREKCIJA/);
  assert.match(source, /DODAJ KOREKTIVNU STAVKU/);
});

test("actor name and time appear on related document and timeline", () => {
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /item\.actorName/);
  assert.match(source, /item\.occurredUtc/);
  assert.match(source, /Stornirao/);
  assert.match(source, /Korigovao/);
  assert.match(source, /Otvori dokument/);
});

test("corrected invoice explains why direct storno is unavailable", () => {
  assert.equal(canStorno("invoice", "Corrected", true), false);
  assert.match(primaryInvoiceStatus("Corrected", "PartiallyPaid"), /^RAČUN KORIGOVAN/);
  const source = readFileSync(new URL("../src/admin/InvoiceDocuments.tsx", import.meta.url), "utf8");
  assert.match(source, /Direktno storno nije dozvoljeno/);
});

test("cancelled invoice has primary storno status and separate credit breakdown", () => {
  assert.equal(primaryInvoiceStatus("Cancelled", "Paid"), "STORNIRAN");
  const source = readFileSync(new URL("../src/admin/FinancePage.tsx", import.meta.url), "utf8");
  assert.match(source, /Prije storna plaćeno/);
  assert.match(source, /Kredit nastao stornom/);
});

test("owner portal explains corrected and cancelled invoices with original PDFs", () => {
  const source = readFileSync(new URL("../src/admin/OwnerFinancePage.tsx", import.meta.url), "utf8");
  assert.match(source, /RAČUN KORIGOVAN/);
  assert.match(source, /RAČUN STORNIRAN/);
  assert.match(source, /Korekcije:/);
  assert.match(source, /Kredit nastao stornom/);
  assert.match(source, /Pogledaj original/);
  assert.match(source, /Pogledaj storno/);
  assert.match(source, /Pogledaj korekciju/);
});
