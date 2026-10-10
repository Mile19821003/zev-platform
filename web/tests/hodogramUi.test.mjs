import assert from "node:assert/strict";
import test from "node:test";
import { draftFromDetail, draftIssues, isDraftChanged, issuesByStep, matrixPayload, roleLabel, validateStep } from "../src/hodogramUi.ts";

const roles = ["Manager", "Accountant", "BuildingRepresentative", "Owner", "OrganizationAdministrator"];
const detail = {
  id: "h1", organizationId: "o1", name: "Hodogram", description: null, isActive: true, revision: 7,
  updatedUtc: "2026-10-10T00:00:00Z", roles, isValid: false, issues: [],
  phases: [{ id: "p1", name: "Planiranje", sortOrder: 0, steps: [
    { id: "s1", name: "Priprema", description: null, sortOrder: 0, cells: { Manager: "R", OrganizationAdministrator: "A" } },
    { id: "s2", name: "Usvajanje", description: null, sortOrder: 1, cells: {} },
  ] }],
};

test("RACI step rule needs at least one R and exactly one A", () => {
  assert.deepEqual(validateStep({ Manager: "R", Owner: "A" }), []);
  assert.deepEqual(validateStep({ Manager: "R", Accountant: "R", Owner: "A", BuildingRepresentative: "C",
    OrganizationAdministrator: "I" }), []);
  assert.equal(validateStep({}).length, 2);
  assert.deepEqual(validateStep({ Manager: "A", Owner: "A" }),
    ["Nedostaje R (izvršava).", "Samo jedna uloga smije biti A."]);
  assert.deepEqual(validateStep({ Manager: "R", Owner: "" }), ["Nedostaje A (odgovoran)."]);
});

test("Matrix draft reports only invalid steps and sends the complete non-empty matrix with revision", () => {
  const draft = draftFromDetail(detail);
  assert.deepEqual(Object.keys(draftIssues(detail, draft)), ["s2"]);
  assert.equal(isDraftChanged(detail, draft), false);
  draft.s2 = { Manager: "R", Accountant: "", OrganizationAdministrator: "A" };
  draft.s1 = { ...draft.s1, Owner: "I" };
  assert.equal(isDraftChanged(detail, draft), true);
  assert.deepEqual(draftIssues(detail, draft), {});
  assert.deepEqual(matrixPayload(detail, draft), { revision: 7, cells: [
    { stepId: "s1", role: "Manager", value: "R" },
    { stepId: "s1", role: "Owner", value: "I" },
    { stepId: "s1", role: "OrganizationAdministrator", value: "A" },
    { stepId: "s2", role: "Manager", value: "R" },
    { stepId: "s2", role: "OrganizationAdministrator", value: "A" },
  ] });
  assert.equal(detail.phases[0].steps[0].cells.Owner, undefined, "draft must not mutate loaded detail");
});

test("Server 422 issues are grouped per step for the matrix rows", () => {
  assert.deepEqual(issuesByStep([
    { stepId: "s2", code: "MISSING_RESPONSIBLE", message: "R" },
    { stepId: "s2", code: "MULTIPLE_ACCOUNTABLE", message: "A" },
    { stepId: "s3", code: "MISSING_ACCOUNTABLE", message: "A0" },
  ]), { s2: ["R", "A"], s3: ["A0"] });
  assert.deepEqual(issuesByStep(undefined), {});
});

test("Role columns use readable labels and fall back to the system role name", () => {
  assert.equal(roleLabel("Manager"), "Upravnik");
  assert.equal(roleLabel("NewRole"), "NewRole");
});
