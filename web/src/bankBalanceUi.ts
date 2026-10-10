// View rules for GET /api/zevs/{zevId}/payments/accounts/{accountId}/balance (bank vs fund balance).
import { formatMoney } from "./currency.ts";
import { formatDate } from "./displaySettings.ts";

export type BankAccountBalance = {
  accountId: string; accountNumber: string; bankName: string | null; currencyCode: string; isActive: boolean;
  bank: {
    balanceSource: "Statement" | "NotProvided";
    reportedBalance: { amount: number; asOf: string | null; importId: string } | null;
    derivedBalance: number | null; derivedMatchesReported: boolean | null;
    credits: number; debits: number; movement: number; statementCount: number;
    gaps: { previousImportId: string; nextImportId: string; previousClosing: number; nextOpening: number; difference: number }[];
  };
  fund: { fundId: string; fundName: string; currencyCode: string; balance: number; fromThisAccount: number;
    fromOtherAccounts: number; manualPayments: number; expenses: number; other: number; otherCurrencyEntries: number };
  reconciliation: { bankMovement: number; fundBalance: number; difference: number; bankOutflowsNotInFund: number;
    manualPaymentsNotInBank: number; expensesNotInBank: number; otherBankAccountsOfFund: number;
    otherFundEntries: number; unexplained: number };
};

export type Tone = "ok" | "info" | "warning";

// Overall state: unexplained difference or a broken statement chain needs attention; an explained difference
// (fees, manual payments, expenses) is normal and only informative.
export function balanceStatus(balance: BankAccountBalance): { tone: Tone; label: string } {
  if (balance.reconciliation.unexplained !== 0)
    return { tone: "warning", label: `Neobjašnjena razlika ${formatMoney(balance.reconciliation.unexplained, balance.currencyCode)}` };
  if (balance.bank.gaps.length > 0) return { tone: "warning", label: "Prekid u nizu izvoda" };
  if (balance.reconciliation.difference === 0) return { tone: "ok", label: "Banka i fond su usklađeni" };
  return { tone: "info", label: "Razlika je objašnjena" };
}

export function bankBalanceLine(balance: BankAccountBalance): string {
  const { reportedBalance } = balance.bank;
  if (!reportedBalance) return "Izvodi ne sadrže saldo (CSV/TXT) — prikazan je samo promet.";
  return `${formatMoney(reportedBalance.amount, balance.currencyCode)}${reportedBalance.asOf ? ` na dan ${formatDate(reportedBalance.asOf)}` : ""}`;
}

// The parts of "bank movement − fund balance"; zero rows are left out.
export function reconciliationRows(balance: BankAccountBalance): { label: string; amount: string }[] {
  const r = balance.reconciliation;
  return ([
    ["Bankovni odlivi koji nisu u knjizi fonda (provizije, isplate)", r.bankOutflowsNotInFund],
    ["Ručne uplate u fondu bez bankovnog izvoda", -r.manualPaymentsNotInBank],
    ["Rashodi knjiženi u fondu", -r.expensesNotInBank],
    ["Uplate na drugim računima istog fonda", -r.otherBankAccountsOfFund],
    ["Ostale stavke fonda", -r.otherFundEntries],
    ["Neobjašnjeno", r.unexplained],
  ] as [string, number][]).filter(([, amount]) => amount !== 0)
    .map(([label, amount]) => ({ label, amount: formatMoney(amount, balance.currencyCode) }));
}

export function gapLines(balance: BankAccountBalance): string[] {
  return balance.bank.gaps.map((gap) => `Završni saldo ${formatMoney(gap.previousClosing, balance.currencyCode)}, `
    + `sljedeći izvod počinje s ${formatMoney(gap.nextOpening, balance.currencyCode)} `
    + `(razlika ${formatMoney(gap.difference, balance.currencyCode)}) — nedostaje izvod ili je saldo pogrešan.`);
}

export { formatDate } from "./displaySettings.ts";
