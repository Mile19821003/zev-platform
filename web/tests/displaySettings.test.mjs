import assert from "node:assert/strict";
import test from "node:test";
import { defaultDisplaySettings, formatDate, formatDateTime, formatMoney, formatNumber, normalizeDisplaySettings,
  setDisplaySettings } from "../src/displaySettings.ts";
import { formatMoney as currencyFormatMoney } from "../src/currency.ts";
import { billingDate, billingMoney } from "../src/billingUi.ts";

const comma = defaultDisplaySettings;
const point = { ...defaultDisplaySettings, decimalSeparator: ".", dateFormat: "YYYY-MM-DD" };

test("money uses the chosen decimal separator, never the browser locale", () => {
  assert.equal(formatMoney(145, "KM", comma), "145,00 KM");
  assert.equal(formatMoney(145, undefined, comma), "145,00 KM");
  assert.equal(formatMoney(1234567.891, "BAM", comma), "1.234.567,89 KM");
  assert.equal(formatMoney(1234567.891, "BAM", point), "1,234,567.89 KM");
  assert.equal(formatMoney(-10, "BAM", comma), "-10,00 KM");
  assert.equal(formatMoney(-0.001, "BAM", comma), "0,00 KM");
  assert.equal(formatMoney(12.5, "eur", comma), "12,50 EUR");
  assert.equal(formatMoney(7, "UNKNOWN", comma), "7,00 [valuta nepoznata]");
  assert.equal(formatNumber(15.005, 2, point), "15.01");
  assert.equal(formatNumber(Number.NaN, 2, comma), "0,00");
});

test("dates follow the chosen format; timestamps are shown in local time", () => {
  assert.equal(formatDate("2026-10-31", comma), "31.10.2026");
  assert.equal(formatDate("2026-10-31", point), "2026-10-31");
  assert.equal(formatDate("2026-10-31T00:00:00", comma), "31.10.2026");
  assert.equal(formatDate(null, comma), "");
  assert.equal(formatDate("nije datum", comma), "nije datum");
  const local = new Date(2026, 9, 31, 7, 5);
  assert.equal(formatDateTime(local.toISOString(), comma), "31.10.2026 07:05");
  assert.equal(formatDateTime(local.toISOString(), point), "2026-10-31 07:05");
});

test("unknown or missing settings fall back to the safe defaults", () => {
  assert.deepEqual(normalizeDisplaySettings(null), defaultDisplaySettings);
  assert.deepEqual(normalizeDisplaySettings({ decimalSeparator: ";", dateFormat: "MM/DD", fontSize: "40px" }), defaultDisplaySettings);
  assert.deepEqual(normalizeDisplaySettings({ decimalSeparator: ".", dateFormat: "YYYY-MM-DD", fontSize: "15px",
    defaultTheme: "Default", updatedAt: "x" }), { decimalSeparator: ".", dateFormat: "YYYY-MM-DD", fontSize: "15px",
    defaultTheme: "Default" });
});

test("existing formatters follow the active settings", () => {
  try {
    setDisplaySettings(point);
    assert.equal(currencyFormatMoney(1234.5, "BAM"), "1,234.50 KM");
    assert.equal(billingMoney(30), "30.00");
    assert.equal(billingDate("2026-10-31"), "2026-10-31");
    setDisplaySettings(comma);
    assert.equal(currencyFormatMoney(1234.5, "BAM"), "1.234,50 KM");
    assert.equal(billingDate(null), "bez kraja");
  } finally { setDisplaySettings(defaultDisplaySettings); }
});
