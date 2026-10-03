export type BillingItem = {
  id: string; code: string; version: number; name: string; basis: string; rate: number;
  fundId: string; fundName: string; validFrom: string; validTo: string | null;
  isActive: boolean; isOptional: boolean; showOnInvoice: boolean; fundIsActive?: boolean;
};

export type BillingIssue = {
  code: string; message: string; propertyUnitCode?: string | null; tariffCode?: string | null;
  fundName?: string | null;
};

export type BillingAdjustment = {
  propertyUnitId: string; tariffVersionId: string; include: boolean | null;
  manualAmount: number | null; note: string | null; reason: string | null;
  description?: string | null; unitOfMeasure?: string | null; quantity?: number | null;
  unitPrice?: number | null; discountType?: string | null; discountValue?: number | null;
};

export type BillingManualLine = {
  id: string; propertyUnitId: string; fundId: string; description: string; unitOfMeasure: string;
  quantity: number; unitPrice: number; discountType: string; discountValue: number | null;
  note: string | null; reason: string;
};

export function manualLineAmount(line: BillingManualLine): number {
  const gross = Math.round(line.quantity * line.unitPrice * 100) / 100;
  const discount = line.discountType === "Percentage" ? Math.round(gross * (line.discountValue ?? 0)) / 100
    : line.discountType === "Fixed" ? line.discountValue ?? 0 : 0;
  return Math.round((gross - discount) * 100) / 100;
}

export function invoiceListView(invoice: { invoiceNumber: string | null; personName: string;
  total: number; paid: number; open: number; paymentStatus: string; documentStatus: string;
  currencyCode?: string }, period: string) {
  return { number: invoice.invoiceNumber ?? "LEGACY OBRAČUN (informativno)", period,
    owner: invoice.personName, total: formatMoney(invoice.total, invoice.currencyCode ?? "UNKNOWN"),
    paid: formatMoney(invoice.paid, invoice.currencyCode ?? "UNKNOWN"),
    open: formatMoney(invoice.open, invoice.currencyCode ?? "UNKNOWN"),
    status: primaryInvoiceStatus(invoice.documentStatus, invoice.paymentStatus) };
}

export function billingBasisLabel(basis: string): string {
  const labels: Record<string, string> = {
    SquareMeter: "Po m²", FixedUnit: "Fiksno po jedinici", RegisteredMember: "Po članu",
    ManualAmount: "Ručni iznos", OneTime: "Jednokratno", Extraordinary: "Vanredno",
  };
  return labels[basis] ?? basis;
}

export function billingDate(value: string | null): string {
  if (!value) return "bez kraja";
  const [year, month, day] = value.slice(0, 10).split("-");
  return `${day}.${month}.${year}.`;
}

export function billingMoney(value: number): string {
  return new Intl.NumberFormat("bs-BA", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

export function billingItemTitle(item: BillingItem): string {
  return `${item.code} | ${item.name}`;
}

export function defaultBillingSelection(items: BillingItem[]): string[] {
  return items.filter((item) => item.isActive && !item.isOptional
    && item.basis !== "ManualAmount" && item.basis !== "Extraordinary").map((item) => item.id);
}

export function similarBillingWarnings(items: BillingItem[], selectedIds: string[]): string[] {
  const selected = new Set(selectedIds);
  const groups = new Map<string, BillingItem[]>();
  for (const item of items.filter((entry) => selected.has(entry.id))) {
    const key = JSON.stringify([item.name.trim().toLocaleLowerCase("bs-BA"), item.basis, item.rate]);
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }
  return [...groups.values()].flatMap((group) => {
    const codes = [...new Set(group.map((item) => item.code))].sort();
    return codes.length > 1
      ? [`${codes.length === 2 ? "Odabrane su dvije slične stavke" : "Odabrane su slične stavke"}: ${codes.join(" i ")}. ` +
        `Provjerite da li ${codes.length === 2 ? "obje" : "sve"} trebaju biti uključene u obračun.`]
      : [];
  });
}

export function billingIssueText(issue: BillingIssue, period: string): string {
  const selectedPeriod = period ? `${period.slice(5, 7)}/${period.slice(0, 4)}` : "odabrani period";
  switch (issue.code) {
    case "PROPERTY_UNIT_NO_ACTIVE_OWNER":
      return `Posebni dio ${issue.propertyUnitCode ?? "—"} nema aktivnog vlasnika za ${selectedPeriod}.`;
    case "NO_ACTIVE_UNITS": return "Nema aktivnih posebnih dijelova za obračun.";
    case "MULTIPLE_ACTIVE_TARIFF_VERSIONS":
      return `Stavka ${issue.tariffCode ?? "—"} ima više aktivnih verzija za ${selectedPeriod}.`;
    case "TARIFF_FUND_INACTIVE": return `Stavka ${issue.tariffCode ?? "—"} nema aktivan fond.`;
    case "SELECTED_TARIFF_INVALID_PERIOD":
      return `Stavka ${issue.tariffCode ?? "odabrana stavka"} nije važeća za ${selectedPeriod}.`;
    case "NO_ITEMS_SELECTED": return "Odaberite najmanje jednu stavku za obračun.";
    case "BILLING_ALREADY_POSTED": return `Obračun za ${selectedPeriod} je već knjižen.`;
    case "INVALID_PERIOD": return "Period mora početi prvog dana mjeseca, a rok ne može biti prije perioda.";
    case "ZEV_NOT_ACTIVE": return "Odabrani ZEV nije aktivan ili nije pronađen.";
    case "SELECTED_UNIT_INVALID": return "Odabrani posebni dio nije aktivan u ovom ZEV-u.";
    case "FUND_BANK_ACCOUNT_MISSING": return `Fond '${issue.fundName ?? "—"}' nema aktivan bankovni račun. Račun se ne može izdati dok se ne podese podaci za plaćanje.`;
    case "BILLING_DRAFT_INVALID": return issue.message;
    default: return "Obračun nije moguće pripremiti. Provjerite podatke.";
  }
}

export function billingDraftSignature(zevId: string, period: string, dueDate: string,
  selectedTariffIds: string[], adjustments: BillingAdjustment[], manualLines: BillingManualLine[] = []): string {
  const orderedAdjustments = [...adjustments].sort((left, right) =>
    `${left.propertyUnitId}:${left.tariffVersionId}`.localeCompare(`${right.propertyUnitId}:${right.tariffVersionId}`));
  return JSON.stringify([zevId, period, dueDate, [...selectedTariffIds].sort(), orderedAdjustments,
    [...manualLines].sort((left, right) => left.id.localeCompare(right.id))]);
}
import { primaryInvoiceStatus } from "./invoiceLifecycleUi.ts";
import { formatMoney } from "./currency.ts";
