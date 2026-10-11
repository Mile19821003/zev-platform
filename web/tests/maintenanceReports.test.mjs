import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { filterRows, frequencyText, monthBars, monthNames, percent, pieSlices, reportExportPath, reportViews, timelinePosition }
  from "../src/maintenanceReports.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("three report views: table, charts, timeline", () => {
  assert.deepEqual(reportViews.map((item) => item.label), ["📋 Analitička tabela", "📊 Grafikoni", "📅 Gantogram"]);
  assert.equal(monthNames.length, 12);
});

test("pie: shares follow the costs, one category is a full circle, nothing without costs", () => {
  const slices = pieSlices([{ category: "Lift", cost: 300, jobs: 2 }, { category: "Krov", cost: 100, jobs: 1 }]);
  assert.deepEqual(slices.map((item) => [item.category, item.share]), [["Lift", 0.75], ["Krov", 0.25]]);
  assert.match(slices[0].path, /^M 90 90 L 90\.00 0\.00 A 90 90 0 1 1 /, "the larger slice starts at 12 o'clock with the large-arc flag");
  assert.match(slices[1].path, / A 90 90 0 0 1 90\.00 0\.00 Z$/, "the last slice closes at 12 o'clock");
  assert.notEqual(slices[0].color, slices[1].color);
  assert.equal(pieSlices([{ category: "Lift", cost: 50, jobs: 1 }])[0].share, 1);
  assert.deepEqual(pieSlices([]), []);
  assert.equal(percent(0.254), "25 %");
});

test("bars share one scale; empty months stay at zero", () => {
  const bars = monthBars([{ month: 1, planned: 4, realized: 2, cost: 0 }, { month: 2, planned: 0, realized: 0, cost: 0 }]);
  assert.deepEqual(bars[0], { month: 1, planned: 1, realized: 0.5, max: 4 });
  assert.deepEqual(bars[1], { month: 2, planned: 0, realized: 0, max: 4 });
  assert.equal(monthBars([{ month: 1, planned: 0, realized: 0, cost: 0 }])[0].max, 1);
});

test("timeline places dates across the year; frequencies are named", () => {
  assert.equal(timelinePosition("2026-01-01", 2026), 0);
  assert.ok(Math.abs(timelinePosition("2026-07-02", 2026) - 49.86) < 0.1);
  assert.ok(timelinePosition("2026-12-31", 2026) > 99.7);
  assert.equal(timelinePosition("2027-03-01", 2026), 100);
  assert.deepEqual([7, 30, 90, 182, 365, 14].map(frequencyText), ["sedmično", "mjesečno", "kvartalno", "polugodišnje", "godišnje", "svakih 14 dana"]);
});

test("analytical table filters by source and searches title, category and contractor", () => {
  const rows = [
    { source: "Plan", title: "Servis lifta", category: "Lift", contractor: "Lift d.o.o." },
    { source: "Prijava", title: "Zamjena sijalice", category: "Rasvjeta", contractor: "Električar Ado" }];
  assert.deepEqual(filterRows(rows, "Prijava", "").map((row) => row.title), ["Zamjena sijalice"]);
  assert.deepEqual(filterRows(rows, "", "ado").map((row) => row.title), ["Zamjena sijalice"]);
  assert.deepEqual(filterRows(rows, "", "lift").map((row) => row.title), ["Servis lifta"]);
  assert.equal(reportExportPath("z1", 2026, "pdf"), "/api/zevs/z1/maintenance/reports/2026.pdf");
});

test("reporting centre: exports for the assembly, SVG charts and the season timeline", () => {
  const page = source("../src/admin/MaintenanceReports.tsx");
  assert.ok(page.includes("Izvezi u PDF") && page.includes("Izvezi u Excel"));
  assert.ok(page.includes('aria-label="Pita grafikon troškova po kategorijama"'));
  assert.ok(page.includes('aria-label="Stubičasti grafikon: planirano i realizovano po mjesecima"'));
  assert.ok(page.includes('className="gantt"'));
  assert.ok(source("../src/admin/MaintenancePage.tsx").includes('<MaintenanceReports token={token} zevId={zevId} canExport={allowed("Maintenance.Export")} />'));
  const api = source("../../src/Zev.Api/Endpoints/MaintenanceReportEndpoints.cs");
  assert.ok(api.includes('reports.MapGet("/summary"') && api.includes('"/{year:int}.xlsx"') && api.includes('"/{year:int}.pdf"'));
});
