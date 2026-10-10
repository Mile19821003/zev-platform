// Display formatting and Bosnian labels shared by the mobile screens (mirrors web/src/currency.ts and uiLabels.ts).
// Number formatting is done by hand so the output does not depend on the Intl data shipped with Hermes.

export function formatAmount(amount: number, currencyCode: string): string {
  const [whole, fraction] = Math.abs(amount).toFixed(2).split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  const sign = amount < 0 && Number(amount.toFixed(2)) !== 0 ? "-" : "";
  return `${sign}${grouped},${fraction} ${currencyLabel(currencyCode)}`;
}

export function currencyLabel(currencyCode: string): string {
  const currency = currencyCode.toUpperCase();
  return currency === "UNKNOWN" ? "[valuta nepoznata]" : currency === "BAM" ? "KM" : currency;
}

// "2026-10-30" -> "30.10.2026."; anything else is shown unchanged.
export function formatDate(value: string | null | undefined): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value ?? "");
  return match ? `${match[3]}.${match[2]}.${match[1]}.` : value ?? "";
}

// "2026-10-01" -> "10/2026" (billing period).
export function formatPeriod(value: string): string {
  const match = /^(\d{4})-(\d{2})/.exec(value);
  return match ? `${match[2]}/${match[1]}` : value;
}

export function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  const pad = (part: number) => String(part).padStart(2, "0");
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}. ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export const documentTypeLabels: Record<string, string> = {
  Contract: "Ugovor", Policy: "Pravilnik", Statute: "Statut", Minutes: "Zapisnik",
  Financial: "Finansijski", Legal: "Pravni", Other: "Ostalo",
};

export const ballotStatusLabels: Record<string, string> = { Draft: "Nacrt", Open: "Otvoreno", Closed: "Zaključeno" };

export const maintenanceStatusLabels: Record<string, string> = {
  Open: "Otvoreno", InProgress: "U toku", Completed: "Završeno", Verified: "Potvrđeno", Cancelled: "Otkazano",
  Due: "Dospjelo",
};

export const invoiceDocumentStatusLabels: Record<string, string> = {
  Issued: "Izdat", Cancelled: "Storniran", Corrected: "Korigovan", Legacy: "Starije zaduženje",
};

export const paymentStatusLabels: Record<string, string> = {
  Paid: "Plaćeno", Unpaid: "Neplaćeno", PartiallyPaid: "Djelimično plaćeno",
};

export const ledgerKindLabels: Record<string, string> = {
  Invoice: "Račun", LegacyCharge: "Starije zaduženje", Correction: "Korekcija", Storno: "Storno",
  Payment: "Uplata", Allocation: "Raspodjela", AllocationRelease: "Ispravka raspoređivanja",
};

export function label(labels: Record<string, string>, value: string | null | undefined): string {
  return value ? labels[value] ?? value : "";
}
