export function paymentLabel(payment: { status: string; personId: string | null;
  bankStatus: string | null; allocated: number; amount: number }): string {
  if (payment.status === "Reversed") return "STORNIRANO / VRAĆENO";
  if (!payment.personId) return "NEPREPOZNATO";
  if (payment.bankStatus === "ManualReview" && payment.allocated === 0) return "ZA POTVRDU";
  if (payment.allocated === payment.amount) return "RASPOREĐENO";
  if (payment.allocated > 0) return "DJELIMIČNO RASPOREĐENO";
  return "KREDIT / PREPLATA";
}

export function activeAllocationCount(allocations: { id: string; amount: number;
  reversesAllocationId: string | null }[]): number {
  return allocations.filter((item) => item.amount > 0
    && !allocations.some((reversal) => reversal.reversesAllocationId === item.id)).length;
}

export function canSubmitPayerCorrection(currentPersonId: string | null, newPersonId: string,
  reason: string, activeCount: number, warningAccepted: boolean): boolean {
  return Boolean(currentPersonId && newPersonId && newPersonId !== currentPersonId
    && reason.trim() && (activeCount === 0 || warningAccepted));
}

export function matchRuleLabel(rule: string | null): string {
  if (rule === "EXACT_REFERENCE_V1") return "Tačan poziv na broj";
  if (rule === "ACCOUNT_REFERENCE_OLDEST_DUE_V1") return "Stabilna referenca jedinice";
  return rule ?? "Ručno povezivanje / bez automatskog matcha";
}

export function bankTransactionLabel(status: string, amount?: number, allocated?: number,
  personName?: string | null): string {
  if (status === "Matched" || status === "PartiallyMatched") {
    if (amount !== undefined && allocated !== undefined) {
      if (allocated >= amount) return "POTPUNO RASPOREĐENO";
      if (allocated > 0) return "DJELIMIČNO RASPOREĐENO / KREDIT OSTAJE";
      return personName ? "IDENTIFIKOVANO, NERASPOREĐENO" : "ZA PROVJERU";
    }
    return status === "Matched" ? "RASPOREĐENO" : "DJELIMIČNO RASPOREĐENO";
  }
  if (status === "ManualReview") return "ZA PROVJERU";
  if (status === "Returned") return "STORNIRANO / VRAĆENO";
  if (status === "Ignored") return "ZANEMARENO";
  return "NEPREPOZNATO";
}

export function allocationStatusLabel(status: string): string {
  return ({ FULLY_ALLOCATED: "POTPUNO RASPOREĐENO",
    PARTIALLY_ALLOCATED_CREDIT_REMAINS: "DJELIMIČNO RASPOREĐENO / KREDIT OSTAJE",
    IDENTIFIED_UNALLOCATED: "IDENTIFIKOVANO, NERASPOREĐENO",
    MANUAL_REVIEW: "ZA PROVJERU", REVERSED: "STORNIRANO / VRAĆENO" } as Record<string, string>)[status] ?? status;
}
