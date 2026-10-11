import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { calendarQuery, canStartWorkOrder, defaultCalendarFilters, rangeFor, rangePresets, summaryText, summaryTone }
  from "../src/maintenanceCalendar.ts";
import { attachmentLabel, completionBody, completionProblem, fileSizeLabel, validateAttachment } from "../src/workOrderDossier.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("quick ranges: this / next month, quarter, year and a custom period", () => {
  assert.deepEqual(rangePresets.map((item) => item.label),
    ["Narednih 60 dana", "Ovaj mjesec", "Sljedeći mjesec", "Tekući kvartal", "Cijela godina", "Proizvoljno"]);
  assert.deepEqual(rangeFor("thisMonth", "2026-02-14"), { from: "2026-02-01", to: "2026-02-28" });
  assert.deepEqual(rangeFor("nextMonth", "2026-12-10"), { from: "2027-01-01", to: "2027-01-31" });
  assert.deepEqual(rangeFor("quarter", "2026-11-05"), { from: "2026-10-01", to: "2026-12-31" });
  assert.deepEqual(rangeFor("year", "2026-10-11"), { from: "2026-01-01", to: "2026-12-31" });
  assert.deepEqual(rangeFor("custom", "2026-10-11", { from: "2026-10-01", to: "2026-10-20" }), { from: "2026-10-01", to: "2026-10-20" });
  assert.deepEqual(rangeFor("default", "2026-10-11"), { from: "", to: "" });
});

test("calendar request carries period, status and category", () => {
  assert.equal(calendarQuery(defaultCalendarFilters, "2026-10-11"), "/calendar");
  assert.equal(calendarQuery({ ...defaultCalendarFilters, preset: "thisMonth", status: "Due", category: "Lift" }, "2026-10-11"),
    "/calendar?from=2026-10-01&to=2026-10-31&status=Due&category=Lift");
});

test("Pokreni radni nalog only for stored, open occurrences without a work order", () => {
  assert.equal(canStartWorkOrder({ id: "s1", status: "Due", workOrderId: null }), true);
  assert.equal(canStartWorkOrder({ id: "s1", status: "Planned", workOrderId: null }), true);
  assert.equal(canStartWorkOrder({ id: "s1", status: "Due", workOrderId: "w1" }), false);
  assert.equal(canStartWorkOrder({ id: null, status: "Planned", workOrderId: null, projected: true }), false);
  assert.equal(canStartWorkOrder({ id: "s1", status: "Completed", workOrderId: null }), false);
});

test("attachments: PDF / JPG / PNG up to 10 MB, labelled by kind", () => {
  assert.equal(validateAttachment({ name: "racun.PDF", type: "application/pdf", size: 9_000_000 }), null);
  assert.match(validateAttachment({ name: "racun.pdf", type: "application/pdf", size: 10_000_001 }), /10 MB/);
  assert.match(validateAttachment({ name: "racun.docx", type: "application/pdf", size: 10 }), /PDF, JPG i PNG/);
  assert.equal(attachmentLabel("PhotoBefore"), "Slika prije");
  assert.equal(attachmentLabel("Invoice"), "Račun / faktura (PDF)");
  assert.equal(fileSizeLabel(1234), "1 KB");
  assert.equal(fileSizeLabel(2_450_000), "2,5 MB");
});

test("completion: required work description, expense needs supplier, invoice, amount and fund", () => {
  const draft = { completedDate: "2026-10-11", workDescription: "Zamijenjen ležaj", invoiceNumber: "", costAmount: "", expenseFund: "Regular",
    supplierId: "", contractorName: " Majstor ", contractorPhone: "", recordExpense: false };
  assert.equal(completionProblem(draft), null);
  assert.match(completionProblem({ ...draft, workDescription: " " }), /Opišite/);
  assert.match(completionProblem({ ...draft, recordExpense: true }), /dobavljač, broj računa, iznos i fond/);
  assert.equal(completionProblem({ ...draft, recordExpense: true, supplierId: "s", invoiceNumber: "R-1", costAmount: "120.5" }), null);
  assert.deepEqual(completionBody({ ...draft, costAmount: "120.5", invoiceNumber: " R-1 " }, "rv"), {
    completedDate: "2026-10-11", workDescription: "Zamijenjen ležaj", invoiceNumber: "R-1", costAmount: 120.5, expenseFund: "Regular",
    supplierId: null, contractorName: "Majstor", contractorPhone: null, recordExpense: false, rowVersion: "rv" });
});

test("home card text and colour", () => {
  assert.equal(summaryText({ due: 3, inProgress: 1, unreadTickets: 0, nextDueOn: null }), "Održavanje: 3 dospjelih obaveza / 1 u toku");
  assert.equal(summaryText({ due: 1, inProgress: 0, unreadTickets: null, nextDueOn: null }), "Održavanje: 1 dospjela obaveza / 0 u toku");
  assert.equal(summaryTone({ due: 1, inProgress: 0, unreadTickets: 0, nextDueOn: null }), "danger");
  assert.equal(summaryTone({ due: 0, inProgress: 2, unreadTickets: 0, nextDueOn: null }), "warning");
  assert.equal(summaryTone({ due: 0, inProgress: 0, unreadTickets: 0, nextDueOn: null }), "ok");
});

test("screens: calendar filters and actions, dossier, home widget", () => {
  const page = source("../src/admin/MaintenancePage.tsx");
  assert.ok(page.includes('aria-label="Filteri kalendara"'));
  assert.ok(page.includes(">Pokreni radni nalog</button>"));
  assert.ok(page.includes('scheduleId: orderSource?.kind === "schedule" ? orderSource.id : null'));
  assert.ok(page.includes("<WorkOrderDossier token={token}"));
  const dossier = source("../src/admin/WorkOrderDossier.tsx");
  assert.ok(dossier.includes("Završen nalog je arhiviran dosije — samo za čitanje."));
  assert.ok(dossier.includes("/complete`, { method: \"PUT\""));
  assert.ok(dossier.includes("Upiši trošak u rashode"));
  for (const file of ["../src/admin/Workspace.tsx", "../src/admin/RoleWorkspace.tsx"]) {
    const workspace = source(file);
    const overview = workspace.slice(workspace.indexOf('{active === "overview" && <>'));
    assert.ok(overview.indexOf("<MaintenanceWidget") > overview.indexOf("<ZevCardGrid")
      && overview.indexOf("<MaintenanceWidget") < overview.indexOf("<ModuleDashboard"), file);
  }
  assert.ok(source("../src/admin/MaintenanceWidget.tsx").includes("sessionStorage.setItem(calendarStatusHandoffKey, \"Due\")"));
});
