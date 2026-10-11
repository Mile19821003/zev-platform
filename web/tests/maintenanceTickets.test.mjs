import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { bellBadge, canSubmitTicket, handlesTickets, ticketPlaces, ticketUnitId, unreadLabel } from "../src/maintenanceTickets.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

test("resident chooses Moj stan or Zajednički prostori; common parts are sent without a unit", () => {
  assert.deepEqual(ticketPlaces.map((item) => item.label), ["Moj stan", "Zajednički prostori"]);
  assert.equal(ticketUnitId("unit", "u1"), "u1");
  assert.equal(ticketUnitId("common", "u1"), null);
  assert.equal(canSubmitTicket("unit", ""), false);
  assert.equal(canSubmitTicket("common", ""), true);
  const page = source("../src/admin/MaintenancePage.tsx");
  assert.ok(page.includes('<fieldset className="ticket-place"><legend>Gdje je kvar?</legend>'));
  assert.ok(page.includes("propertyUnitId: role === \"owner\" ? ticketUnitId(place, unitId) : unitId || null"));
});

test("bell text and badge: Bosnian plural, capped at 9+", () => {
  assert.equal(unreadLabel(0), "Nema novih prijava kvarova");
  assert.equal(unreadLabel(1), "1 nova prijava kvara");
  assert.equal(unreadLabel(3), "3 nove prijave kvara");
  assert.equal(unreadLabel(5), "5 novih prijava kvara");
  assert.equal(unreadLabel(12), "12 novih prijava kvara");
  assert.equal(unreadLabel(21), "21 nova prijava kvara");
  assert.equal(unreadLabel(24), "24 nove prijave kvara");
  assert.equal(bellBadge(0), null);
  assert.equal(bellBadge(4), "4");
  assert.equal(bellBadge(17), "9+");
});

test("only those who handle maintenance see the bell and mark reports read", () => {
  assert.equal(handlesTickets(["Maintenance.View", "Maintenance.Create"]), false);
  assert.equal(handlesTickets(["Maintenance.Edit"]), true);
  assert.equal(handlesTickets(["Maintenance.Approve"]), true);
  assert.equal(handlesTickets(undefined, true), true);
  const bell = source("../src/admin/NotificationBell.tsx");
  assert.ok(bell.includes("/maintenance/tickets/unread-count"));
  assert.ok(source("../src/admin/Workspace.tsx").includes('<NotificationBell token={token} zevId={activeZev.id} onOpen={() => setActive("maintenance")} />'));
  assert.ok(source("../src/admin/RoleWorkspace.tsx").includes("{activeZev && handlesTickets(activeZev.permissions) && <NotificationBell"));
  const page = source("../src/admin/MaintenancePage.tsx");
  assert.ok(page.includes('<span className="badge ticket-new">Novo</span>'));
  assert.ok(page.includes("/mark-as-read`, { method: \"POST\" }"));
});

test("notification address is part of the system settings screen", () => {
  const settings = source("../src/admin/SystemSettingsPage.tsx");
  assert.ok(settings.includes("E-mail za obavještenja (nove prijave kvarova)"));
  assert.ok(settings.includes("defaultAdminEmail: adminEmail.trim() || null"));
});
