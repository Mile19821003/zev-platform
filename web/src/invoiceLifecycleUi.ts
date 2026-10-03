export type DocumentStatus = "Issued" | "Cancelled" | "Corrected" | "Legacy";
export type CorrectionType = "Credit" | "Debit";
export type CorrectionLineDraft = { originalInvoiceLineId: string | null; fundId: string | null;
  description: string | null; amount: number };

export function documentStatusLabel(status: string): string {
  return ({ Issued: "IZDAT", Cancelled: "STORNIRAN", Corrected: "KORIGOVAN",
    Legacy: "LEGACY OBRAČUN" } as Record<string, string>)[status] ?? status;
}

export function canStorno(invoiceId: string | null, status: string, allowed: boolean): boolean {
  return Boolean(invoiceId) && status === "Issued" && allowed;
}

export function canCorrect(invoiceId: string | null, status: string, allowed: boolean): boolean {
  return Boolean(invoiceId) && ["Issued", "Corrected"].includes(status) && allowed;
}

export function primaryInvoiceStatus(status: string, paymentStatus: string): string {
  if (status === "Cancelled") return "STORNIRAN";
  if (status === "Corrected") return `RAČUN KORIGOVAN · Plaćanje: ${paymentStatus}`;
  return `${paymentStatus} · ${documentStatusLabel(status)}`;
}

export function timelineEventLabel(type: string): string {
  return ({ Invoice: "RAČUN IZDAT", Payment: "UPLATA", Credit: "KOREKCIJA – UMANJENJE",
    Debit: "KOREKCIJA – DODATNO ZADUŽENJE", Storno: "STORNO",
    AllocationRelease: "OSLOBOĐEN KREDIT",
    ManualAllocationReversal: "ISPRAVKA RASPOREĐIVANJA" } as Record<string, string>)[type] ?? type;
}

export function paymentSplitLabel(total: number, appliedToInvoice: number, currencyCode: string): string {
  return `Uplata ukupno: ${formatMoney(total, currencyCode)} · Na ovaj račun: ${formatMoney(appliedToInvoice, currencyCode)}`;
}

export function coalesceTimelineEvents<Event extends { type: string; sourceId: string; amount: number;
  financialDocumentId?: string | null }>(events: Event[]): Event[] {
  const grouped = new Map<string, Event>();
  for (const event of events) {
    const key = event.type === "Payment" ? `Payment:${event.sourceId}`
      : event.type === "AllocationRelease"
        ? `AllocationRelease:${event.financialDocumentId ?? ""}:${event.sourceId}`
        : `${event.type}:${event.sourceId}`;
    const previous = grouped.get(key);
    grouped.set(key, previous ? { ...previous, amount: previous.amount + event.amount } : event);
  }
  return [...grouped.values()];
}

export function correctionSignedTotal(type: CorrectionType, lines: CorrectionLineDraft[]): number {
  const total = lines.reduce((sum, line) => sum + line.amount, 0);
  return type === "Credit" ? -total : total;
}

export function validateCorrectionLines(type: CorrectionType, lines: CorrectionLineDraft[]): string | null {
  if (lines.length < 1 || lines.length > 100) return "Odaberite 1–100 korektivnih stavki.";
  if (lines.some((line) => !Number.isFinite(line.amount) || line.amount <= 0
    || Number(line.amount.toFixed(2)) !== line.amount))
    return "Svaka stavka mora imati pozitivan iznos sa najviše dvije decimale.";
  if (type === "Credit" && lines.some((line) => !line.originalInvoiceLineId))
    return "Umanjenje mora biti vezano za izvornu stavku računa.";
  if (lines.some((line) => !line.originalInvoiceLineId && (!line.fundId || !line.description?.trim())))
    return "Nova stavka mora imati fond i opis.";
  if (correctionSignedTotal(type, lines) === 0) return "Ukupna korekcija ne može biti nula.";
  return null;
}

export function validateDocumentReason(reason: string): string | null {
  return reason.trim().length > 0 && reason.trim().length <= 500
    ? null : "Razlog je obavezan (najviše 500 znakova).";
}

export function validateCorrection(type: CorrectionType, lineId: string, fundId: string,
  description: string, amount: string): string | null {
  const value = Number(amount);
  if (!Number.isFinite(value) || value <= 0 || Number(value.toFixed(2)) !== value)
    return "Iznos mora biti pozitivan, sa najviše dvije decimale.";
  if (type === "Credit" && !lineId) return "Za umanjenje odaberite izvornu stavku računa.";
  if (!lineId && (!fundId || !description.trim())) return "Nova stavka mora imati fond i opis.";
  return null;
}
import { formatMoney } from "./currency.ts";
