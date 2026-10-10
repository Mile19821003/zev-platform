// Owner finance card as returned by GET /api/me/finance/{zevId} (one card per linked person) and its
// mapping to what the mobile screen shows.
import { formatAmount, formatDate, formatPeriod, invoiceDocumentStatusLabels, label, paymentStatusLabels } from "./format";

export type CurrencyBalance = { currencyCode: string; totalCharges: number; totalPayments: number;
  open: number; credit: number; netBalance: number };
export type LedgerEvent = { date: string; kind: string; document: string; description: string;
  currencyCode: string; debt: number; payment: number; creditDelta: number; chargeId: string | null;
  paymentId: string | null; runningBalance: number; propertyUnitId: string | null; period: string | null };
export type CardInvoice = { id: string; propertyUnitId: string; invoiceNumber: string; period: string;
  issueDate: string; dueDate: string; amount: number; paid: number; currency: string; effectiveAmount: number;
  open: number; documentStatus: string; paymentStatus: string };
export type OwnerCard = { personId: string; balances: CurrencyBalance[]; events: LedgerEvent[];
  invoices: CardInvoice[]; page: number; pageSize: number; totalCount: number };

export type SlipTarget = { chargeId: string; label: string; path: string };
export type BalanceView = { key: string; currencyCode: string; title: string; detail: string };
export type InvoiceView = { id: string; number: string; title: string; detail: string; status: string;
  open: boolean; slips: SlipTarget[] };
export type PaymentView = { key: string; text: string };
export type FinanceView = { balances: BalanceView[]; invoices: InvoiceView[]; legacyCharges: InvoiceView[];
  payments: PaymentView[]; partial: boolean };

export const financePageSize = 100;

// Fails loudly when the API shape changes instead of crashing while rendering (the old screen expected
// `charges`/`payments` arrays that the API never returned).
export function parseOwnerCards(json: unknown): OwnerCard[] {
  if (!Array.isArray(json)) throw new Error("Neočekivan odgovor finansijske kartice.");
  return json.map((card) => {
    if (!isRecord(card) || typeof card.personId !== "string" || !Array.isArray(card.balances)
      || !Array.isArray(card.events) || !Array.isArray(card.invoices)) {
      throw new Error("Neočekivan odgovor finansijske kartice.");
    }
    return { personId: card.personId, balances: card.balances as CurrencyBalance[],
      events: card.events as LedgerEvent[], invoices: card.invoices as CardInvoice[],
      page: Number(card.page ?? 1), pageSize: Number(card.pageSize ?? card.events.length),
      totalCount: Number(card.totalCount ?? card.events.length) };
  });
}

// Events are ordered oldest first, so after reading page 1 the newest entries are on the last page.
export function lastPage(cards: OwnerCard[], pageSize = financePageSize): number {
  const total = Math.max(0, ...cards.map((card) => card.totalCount));
  return Math.max(1, Math.ceil(total / pageSize));
}

export function slipPath(zevId: string, chargeId: string): string {
  return `/api/zevs/${zevId}/charges/${chargeId}/payment-slip.pdf`;
}

export function toFinanceView(zevId: string, cards: OwnerCard[]): FinanceView {
  const balances = new Map<string, CurrencyBalance>();
  for (const balance of cards.flatMap((card) => card.balances)) {
    const key = balance.currencyCode.toUpperCase();
    const current = balances.get(key);
    balances.set(key, current ? { currencyCode: current.currencyCode,
      totalCharges: current.totalCharges + balance.totalCharges,
      totalPayments: current.totalPayments + balance.totalPayments, open: current.open + balance.open,
      credit: current.credit + balance.credit, netBalance: current.netBalance + balance.netBalance } : balance);
  }
  const events = cards.flatMap((card) => card.events);
  const invoices = cards.flatMap((card) => card.invoices)
    .sort((left, right) => right.period.localeCompare(left.period) || right.invoiceNumber.localeCompare(left.invoiceNumber))
    .map((invoice): InvoiceView => {
      const open = invoice.open > 0 && invoice.documentStatus !== "Cancelled";
      const lines = events.filter((event) => event.kind === "Invoice" && event.document === invoice.invoiceNumber
        && event.chargeId);
      return { id: invoice.id, number: invoice.invoiceNumber,
        title: `${invoice.invoiceNumber} · ${formatPeriod(invoice.period)}`,
        detail: `Iznos ${formatAmount(invoice.effectiveAmount, invoice.currency)} · otvoreno ${formatAmount(invoice.open, invoice.currency)} · rok ${formatDate(invoice.dueDate)}`,
        status: invoice.documentStatus === "Cancelled" ? label(invoiceDocumentStatusLabels, invoice.documentStatus)
          : `${label(paymentStatusLabels, invoice.paymentStatus)} · ${label(invoiceDocumentStatusLabels, invoice.documentStatus)}`,
        open,
        slips: open ? lines.map((event) => ({ chargeId: event.chargeId!,
          label: lines.length > 1 ? `Uplatnica · ${event.description}` : "Uplatnica (PDF)",
          path: slipPath(zevId, event.chargeId!) })) : [] };
    });
  const anyOpen = [...balances.values()].some((balance) => balance.open > 0);
  const legacyCharges = events.filter((event) => event.kind === "LegacyCharge" && event.chargeId)
    .reverse()
    .map((event): InvoiceView => ({ id: event.chargeId!, number: event.document,
      title: `${event.description || "Starije zaduženje"}${event.period ? ` · ${formatPeriod(event.period)}` : ""}`,
      detail: `Iznos ${formatAmount(event.debt, event.currencyCode)} · ${formatDate(event.date)}`,
      status: "Starije zaduženje", open: anyOpen,
      slips: anyOpen ? [{ chargeId: event.chargeId!, label: "Uplatnica (PDF)", path: slipPath(zevId, event.chargeId!) }] : [] }));
  const payments = events.filter((event) => event.kind === "Payment")
    .reverse()
    .map((event, index) => ({ key: event.paymentId ?? `payment-${index}`,
      text: `${formatDate(event.date)} · ${formatAmount(event.payment, event.currencyCode)}` }));
  return {
    balances: [...balances.entries()].map(([key, balance]) => ({ key, currencyCode: balance.currencyCode,
      title: balance.open > 0 ? `Za uplatu: ${formatAmount(balance.open, balance.currencyCode)}`
        : balance.credit > 0 ? `Pretplata: ${formatAmount(balance.credit, balance.currencyCode)}` : "Izmireno",
      detail: `Zaduženo ${formatAmount(balance.totalCharges, balance.currencyCode)} · uplaćeno ${formatAmount(balance.totalPayments, balance.currencyCode)}`
        + (balance.open > 0 && balance.credit > 0 ? ` · pretplata ${formatAmount(balance.credit, balance.currencyCode)}` : "") })),
    invoices, legacyCharges, payments,
    partial: cards.some((card) => card.totalCount > card.events.length),
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

// Loads the cards with the newest ledger entries: page 1 gives the totals, the last page the latest events.
export async function loadOwnerCards(get: (path: string) => Promise<unknown>, zevId: string): Promise<OwnerCard[]> {
  const path = (page: number) => `/api/me/finance/${zevId}?page=${page}&pageSize=${financePageSize}`;
  const first = parseOwnerCards(await get(path(1)));
  const last = lastPage(first);
  return last > 1 ? parseOwnerCards(await get(path(last))) : first;
}
