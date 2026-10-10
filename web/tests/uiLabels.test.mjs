import assert from "node:assert/strict";
import test from "node:test";
import { announcementScopeLabels, ballotStatusLabels, documentTypeLabels, label, maintenanceStatusLabels,
  priorityLabels, visibilityLabels } from "../src/uiLabels.ts";

test("Document type and access values are shown in Bosnian", () => {
  assert.equal(label(documentTypeLabels, "Other"), "Ostalo");
  assert.equal(label(documentTypeLabels, "Minutes"), "Zapisnik");
  assert.equal(label(visibilityLabels, "PersonOnly"), "Samo lice");
  assert.equal(label(visibilityLabels, "ZevMembers"), "Članovi ZEV-a");
  assert.equal(label(visibilityLabels, "ManagersOnly"), "Samo upravnik");
  assert.equal(label(announcementScopeLabels, "Zev"), "Cijeli ZEV");
});

test("Ballot and maintenance states are translated; unknown values pass through", () => {
  assert.equal(label(ballotStatusLabels, "Open"), "Otvoreno");
  assert.equal(label(maintenanceStatusLabels, "InProgress"), "U toku");
  assert.equal(label(priorityLabels, "Urgent"), "Hitno");
  assert.equal(label(documentTypeLabels, "NewType"), "NewType");
  assert.equal(label(documentTypeLabels, null), "");
});
