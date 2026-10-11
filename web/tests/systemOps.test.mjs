import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { buildClientReport, createReporter } from "../src/errorReporting.ts";
import { errorDraftChanged, errorFilters, errorStatusLabel, sortVersions, sourceBadge, versionNoteChanged, versionStatusLabel }
  from "../src/systemOpsUi.ts";
import { lookupFallbacks } from "../src/lookupOptions.ts";
import { iconCatalog } from "../src/navigationModel.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("client error report carries the error, stack and trims long fields", () => {
  const error = new TypeError("x".repeat(2500));
  const report = buildClientReport(error, "Greška prikaza", "http://app/page", "    in Panel");
  assert.equal(report.errorType, "TypeError");
  assert.equal(report.message.length, 2000);
  assert.match(report.stackTrace, /TypeError/);
  assert.match(report.stackTrace, /in Panel$/);
  assert.equal(report.url, "http://app/page");
  const plain = buildClientReport("pukao fetch", "Neobrađeno", null);
  assert.deepEqual([plain.errorType, plain.message, plain.stackTrace, plain.url], ["string", "pukao fetch", null, null]);
  assert.equal(buildClientReport({ code: 7 }, "t", null).message, '{"code":7}');
});

test("reporter deduplicates, throttles per minute and never throws", () => {
  let clock = 0;
  const sent = [];
  const report = createReporter(async (value) => { sent.push(value.message); }, () => clock, 2);
  const make = (message) => ({ title: "t", errorType: "Error", message, stackTrace: null, url: null });
  assert.equal(report(make("a")), true);
  assert.equal(report(make("a")), false, "same error is sent once");
  assert.equal(report(make("b")), true);
  assert.equal(report(make("c")), false, "limit per minute");
  clock = 61_000;
  assert.equal(report(make("c")), true, "window moved on");
  assert.deepEqual(sent, ["a", "b", "c"]);
  const failing = createReporter(() => { throw new Error("offline"); });
  assert.equal(failing(make("d")), true);
  const rejecting = createReporter(() => Promise.reject(new Error("offline")));
  assert.equal(rejecting(make("e")), true);
});

test("error list view rules", () => {
  assert.deepEqual(errorFilters.map((item) => item.label), ["Aktuelni", "Završeni", "Svi"]);
  assert.deepEqual(errorFilters.map((item) => item.value), ["active", "resolved", "all"]);
  assert.equal(sourceBadge("Frontend").className, "badge frontend");
  assert.equal(sourceBadge("Backend").label, "Backend");
  assert.equal(errorStatusLabel("Resolved"), "Završen");
  assert.equal(errorStatusLabel("Active"), "Aktuelan");
  const item = { status: "Active", adminNotes: null };
  assert.equal(errorDraftChanged(item, false, "  "), false);
  assert.equal(errorDraftChanged(item, true, ""), true);
  assert.equal(errorDraftChanged({ status: "Resolved", adminNotes: "ok" }, true, "ok"), false);
  assert.equal(errorDraftChanged({ status: "Resolved", adminNotes: "ok" }, true, "ok, provjereno"), true);
});

test("versions are sorted numerically, newest first, and notes only save when changed", () => {
  const versions = ["v2.9", "v2.10", "v2.7", "v2.12", "v2.11", "v2.8"].map((version) => ({ version, adminNotes: null }));
  assert.deepEqual(sortVersions(versions).map((item) => item.version), ["v2.12", "v2.11", "v2.10", "v2.9", "v2.8", "v2.7"]);
  assert.equal(versionNoteChanged({ adminNotes: "a" }, undefined), false);
  assert.equal(versionNoteChanged({ adminNotes: "a" }, "a "), false);
  assert.equal(versionNoteChanged({ adminNotes: null }, "nova"), true);
  assert.equal(versionStatusLabel("installed"), "Instalirana");
});

test("version seed covers v2.7 to the current CHANGELOG version", () => {
  const seeded = [...source("../../src/Zev.Api/Domain/SystemOperationsEntities.cs").matchAll(/Row\("(v\d+\.\d+)"/g)]
    .map((match) => match[1]);
  assert.deepEqual(seeded, ["v2.7", "v2.8", "v2.9", "v2.10", "v2.11", "v2.12"]);
});

test("document categories match the backend document type enum", () => {
  const enumBody = source("../../src/Zev.Api/Domain/CommunityEntities.cs").match(/enum ManagedDocumentType\s*\{([^}]*)\}/)[1];
  const backendTypes = enumBody.split(",").map((value) => value.replace(/\/\/.*$/gm, "").trim()).filter(Boolean);
  assert.deepEqual(lookupFallbacks.DOCUMENT_CATEGORY.map((item) => item.code).sort(), [...backendTypes].sort());
  assert.ok(backendTypes.includes("InsurancePolicy") && backendTypes.includes("Decision"));
  const community = source("../src/admin/CommunityPage.tsx");
  assert.match(community, /<LookupSelect typeCode="DOCUMENT_CATEGORY"/);
  assert.doesNotMatch(community, /\['Contract', 'Policy'/);
});

test("errors and versions are administration modules wired into the workspace", () => {
  const catalog = iconCatalog();
  for (const [key, label] of [["errors", "Greške u sistemu"], ["versions", "Istorija verzija"]]) {
    const entry = catalog.find((item) => item.moduleKey === key);
    assert.equal(entry?.category, "Administracija i sistem");
    assert.equal(entry?.label, label);
  }
  const workspace = source("../src/admin/Workspace.tsx");
  assert.match(workspace, /active === "errors" && <SystemErrorsPage/);
  assert.match(workspace, /active === "versions" && <VersionHistoryPage/);
  const main = source("../src/main.tsx");
  assert.match(main, /<ErrorBoundary><App \/><\/ErrorBoundary>/);
  assert.match(main, /installGlobalErrorReporting\(\)/);
});
