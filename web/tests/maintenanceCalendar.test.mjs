import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { canCompleteSchedule, scheduleBadgeClass, scheduleStatusLabel } from "../src/maintenanceCalendar.ts";
import { formatDate, setDisplaySettings, defaultDisplaySettings } from "../src/displaySettings.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("calendar statuses: Planirano, Dospjelo, U toku, Završeno", () => {
  assert.deepEqual(["Planned", "Due", "InProgress", "Completed", "Cancelled"].map(scheduleStatusLabel),
    ["Planirano", "Dospjelo", "U toku", "Završeno", "Otkazano"]);
  assert.equal(scheduleBadgeClass("Planned"), "badge schedule-planned");
  assert.equal(scheduleBadgeClass("InProgress"), "badge schedule-inprogress");
  assert.equal(canCompleteSchedule("Due"), true);
  assert.equal(canCompleteSchedule("InProgress"), true);
  assert.equal(canCompleteSchedule("Planned"), false, "a future occurrence waits for its date");
  assert.equal(canCompleteSchedule("Completed"), false);
  const css = source("../src/admin.css");
  for (const status of ["planned", "due", "inprogress", "completed", "cancelled"]) assert.ok(css.includes(`.badge.schedule-${status}{`), status);
});

test("the API status rule matches: open occurrences are Planned before their date, Due from it", () => {
  const service = source("../../src/Zev.Api/Maintenance/MaintenanceScheduleService.cs");
  assert.ok(service.includes('status == ScheduleStatus.Due && dueOn > today ? "Planned" : status.ToString()'));
  assert.ok(service.includes("public const int HorizonDays = 60;"));
});

test("calendar and work order dates follow the system date format", () => {
  setDisplaySettings({ ...defaultDisplaySettings, dateFormat: "DD.MM.YYYY" });
  assert.equal(formatDate("2026-10-14"), "14.10.2026");
  const page = source("../src/admin/MaintenancePage.tsx");
  assert.ok(page.includes("<td>{formatDate(item.dueOn)}</td>"));
  assert.ok(page.includes('<td>{item.dueOn ? formatDate(item.dueOn) : "—"}</td>'));
  assert.doesNotMatch(page, /<td>\{item\.dueOn\}<\/td>/, "no raw YYYY-MM-DD dates");
  assert.ok(page.includes("<span className={scheduleBadgeClass(item.status)}>{scheduleStatusLabel(item.status)}</span>"));
  assert.ok(page.includes('body: JSON.stringify({ status: "Completed"'));
});

test("periodic plans: status, interval and actions (deactivate, delete with confirmation)", async () => {
  const { planStatusLabel, planFrequencyText, planDeleteWarning } = await import("../src/maintenanceCalendar.ts");
  assert.equal(planStatusLabel(true), "Aktivan");
  assert.equal(planStatusLabel(false), "Deaktiviran");
  assert.deepEqual([1, 3, 7, 30].map(planFrequencyText), ["svaki dan", "svakih 3 dana", "svake sedmice", "svakih 30 dana"]);
  assert.match(planDeleteWarning, /Obrisani redovi/);
  const page = source("../src/admin/MaintenancePage.tsx");
  assert.ok(page.includes("/maintenance/plans/${plan.id}`, { method: \"DELETE\" }"));
  assert.ok(page.includes("/maintenance/plans/${plan.id}/toggle-active`, { method: \"PUT\" }"));
  assert.ok(page.includes('{plan.isActive ? "Deaktiviraj" : "Aktiviraj"}'));
  assert.ok(page.includes('role="dialog" aria-modal="true" aria-labelledby="plan-delete-title"'), "delete asks in a dialog first");
  assert.ok(page.includes("onClick={() => setPlanToDelete(plan)}>Obriši</button>"), "Obriši only opens the dialog");
  const api = source("../../src/Zev.Api/Maintenance/MaintenanceScheduleService.cs");
  assert.ok(api.includes('"Plan ima izvršene radne naloge ili troškove. Deaktivirajte ga umjesto brisanja."'));
});
