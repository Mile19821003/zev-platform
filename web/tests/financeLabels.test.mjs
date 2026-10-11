import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { bankImportStatusLabels, bankTransactionStatusLabels, expenseStatusLabels, fundKindLabels, label,
  legalCaseStatusLabels, paymentSourceLabels, reminderKindLabels } from "../src/uiLabels.ts";

const source = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const enumMembers = (file, name) => {
  const domain = source(`../../src/Zev.Api/Domain/${file}`);
  return domain.slice(domain.indexOf(`public enum ${name}`)).match(/\{([^}]*)\}/)[1]
    .split(/[,\n]/).map((item) => item.replace(/\/\/.*/, "").trim()).filter(Boolean);
};

test("finance codes have Bosnian labels for every backend enum member", () => {
  for (const [labels, file, name] of [
    [expenseStatusLabels, "ExpenseEntities.cs", "ExpenseStatus"],
    [reminderKindLabels, "ExpenseEntities.cs", "ReminderKind"],
    [legalCaseStatusLabels, "ExpenseEntities.cs", "LegalCaseStatus"],
    [fundKindLabels, "FinanceEntities.cs", "FundKind"],
    [bankTransactionStatusLabels, "PaymentEntities.cs", "BankTransactionStatus"],
    [bankImportStatusLabels, "PaymentEntities.cs", "BankImportStatus"],
    [paymentSourceLabels, "PaymentEntities.cs", "PaymentSourceType"],
  ]) assert.deepEqual(Object.keys(labels).sort(), enumMembers(file, name).sort(), name);
  assert.deepEqual([label(expenseStatusLabels, "Posted"), label(legalCaseStatusLabels, "Settled"),
    label(bankTransactionStatusLabels, "ManualReview"), label(paymentSourceLabels, "Manual"), label(fundKindLabels, "Reserve")],
    ["Proknjiženo", "Riješen nagodbom", "Za provjeru", "Ručna uplata", "Rezervni"]);
});

test("finance pages show labels and keep codes as option values", () => {
  const checks = {
    "ExpensesPage.tsx": ["<td>{label(expenseStatusLabels, item.status)}</td>"],
    "LegalPage.tsx": ["<td>{label(reminderKindLabels, item.kind)}</td>", ": label(legalCaseStatusLabels, item.status)}",
      "Object.entries(legalCaseStatusLabels).map(([code, name]) => <option key={code} value={code}>{name}</option>)",
      "Object.entries(reminderKindLabels).map(([code, name]) => <option key={code} value={code}>{name}</option>)"],
    "PaymentsPage.tsx": ["<td>{label(bankTransactionStatusLabels, item.status)}</td>", "<td>{label(bankImportStatusLabels, item.status)}</td>"],
    "BankTransactionsPage.tsx": ["Object.entries(bankTransactionStatusLabels)", "<option key={code} value={code}>{name}</option>"],
    "PaymentWorkspace.tsx": ["<td>{label(paymentSourceLabels, item.sourceType)}</td>"],
    "FinancePage.tsx": ["({label(fundKindLabels, fund.kind)})",
      "Object.entries(fundKindLabels).map(([code, name]) => <option key={code} value={code}>{name}</option>)"],
  };
  for (const [page, needles] of Object.entries(checks)) {
    const text = source(`../src/admin/${page}`);
    for (const needle of needles) assert.ok(text.includes(needle), `${page}: ${needle}`);
    assert.ok(!/<td>\{item\.(status|kind|sourceType)\}<\/td>/.test(text), `${page}: raw code in a cell`);
  }
});
