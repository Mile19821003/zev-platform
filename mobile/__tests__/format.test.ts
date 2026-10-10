import { ballotStatusLabels, documentTypeLabels, formatAmount, formatDate, formatPeriod, formatTimestamp, label,
  maintenanceStatusLabels, paymentStatusLabels } from "../src/format";

describe("formatAmount", () => {
  it.each([
    [30, "BAM", "30,00 KM"],
    [1234.5, "BAM", "1.234,50 KM"],
    [1234567.891, "bam", "1.234.567,89 KM"],
    [0, "BAM", "0,00 KM"],
    [-5, "BAM", "-5,00 KM"],
    [-0.001, "BAM", "0,00 KM"],
    [12.3, "EUR", "12,30 EUR"],
    [7, "UNKNOWN", "7,00 [valuta nepoznata]"],
  ])("%p %s -> %s", (amount, currency, expected) => {
    expect(formatAmount(amount, currency)).toBe(expected);
  });
});

describe("dates", () => {
  it("formats ISO dates and periods the Bosnian way", () => {
    expect(formatDate("2026-10-30")).toBe("30.10.2026.");
    expect(formatDate("2026-10-30T08:00:00Z")).toBe("30.10.2026.");
    expect(formatDate(null)).toBe("");
    expect(formatPeriod("2026-10-01")).toBe("10/2026");
  });

  it("formats cache timestamps in local time", () => {
    const local = new Date(2026, 9, 10, 7, 5);
    expect(formatTimestamp(local.toISOString())).toBe("10.10.2026. 07:05");
    expect(formatTimestamp("nije datum")).toBe("nije datum");
  });
});

describe("labels", () => {
  it("translates statuses like the web and keeps unknown values", () => {
    expect(label(maintenanceStatusLabels, "InProgress")).toBe("U toku");
    expect(label(ballotStatusLabels, "Open")).toBe("Otvoreno");
    expect(label(documentTypeLabels, "Minutes")).toBe("Zapisnik");
    expect(label(paymentStatusLabels, "PartiallyPaid")).toBe("Djelimično plaćeno");
    expect(label(maintenanceStatusLabels, "Novi")).toBe("Novi");
    expect(label(maintenanceStatusLabels, undefined)).toBe("");
  });
});
