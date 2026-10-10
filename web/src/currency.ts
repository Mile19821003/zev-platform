// Money is formatted centrally from the platform settings (decimal separator), independent of the browser locale.
export { formatMoney } from "./displaySettings.ts";

export function ownerLedgerLabel(kind: string): string {
  return ({ Invoice: "Račun", LegacyCharge: "Starije zaduženje", Correction: "Korekcija",
    Storno: "Storno", Payment: "Uplata", Allocation: "Raspodjela",
    AllocationRelease: "Ispravka raspoređivanja" } as Record<string, string>)[kind] ?? kind;
}
