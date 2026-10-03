export function formatMoney(amount: number, currencyCode: string): string {
  const value = new Intl.NumberFormat("bs-BA", { minimumFractionDigits: 2,
    maximumFractionDigits: 2 }).format(amount);
  const currency = currencyCode.toUpperCase();
  return `${value} ${currency === "UNKNOWN" ? "[valuta nepoznata]"
    : currency === "BAM" ? "KM" : currency}`;
}

export function ownerLedgerLabel(kind: string): string {
  return ({ Invoice: "Račun", LegacyCharge: "Starije zaduženje", Correction: "Korekcija",
    Storno: "Storno", Payment: "Uplata", Allocation: "Raspodjela",
    AllocationRelease: "Ispravka raspoređivanja" } as Record<string, string>)[kind] ?? kind;
}
