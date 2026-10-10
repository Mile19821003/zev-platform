// Central number / money / date formatting driven by the platform settings (GET /api/system/settings).
// It deliberately does not use Intl or toLocaleString: browsers without the bs-BA locale data (e.g. Edge in
// some environments) would otherwise print "145.00 KM" instead of "145,00 KM".
export type DecimalSeparator = "," | ".";
export type DateFormat = "DD.MM.YYYY" | "YYYY-MM-DD";
export type DisplaySettings = { decimalSeparator: DecimalSeparator; dateFormat: DateFormat; fontSize: string;
  defaultTheme: string };

export const defaultDisplaySettings: DisplaySettings = { decimalSeparator: ",", dateFormat: "DD.MM.YYYY",
  fontSize: "13px", defaultTheme: "Default" };

const fontSizes = ["13px", "14px", "15px"];
let current: DisplaySettings = defaultDisplaySettings;

export function setDisplaySettings(settings: DisplaySettings): void { current = settings; }
export function getDisplaySettings(): DisplaySettings { return current; }

// Accepts an API response (or anything) and keeps only known values.
export function normalizeDisplaySettings(raw: unknown): DisplaySettings {
  const value = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  return {
    decimalSeparator: value.decimalSeparator === "." ? "." : ",",
    dateFormat: value.dateFormat === "YYYY-MM-DD" ? "YYYY-MM-DD" : "DD.MM.YYYY",
    fontSize: typeof value.fontSize === "string" && fontSizes.includes(value.fontSize) ? value.fontSize : defaultDisplaySettings.fontSize,
    defaultTheme: typeof value.defaultTheme === "string" ? value.defaultTheme : "Default",
  };
}

// 1234.5 → "1.234,50" (decimal comma) or "1,234.50" (decimal point).
export function formatNumber(value: number, decimals = 2, settings: DisplaySettings = current): string {
  const rounded = Number.isFinite(value) ? value : 0;
  const [whole, fraction] = Math.abs(rounded).toFixed(decimals).split(".");
  const group = settings.decimalSeparator === "," ? "." : ",";
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, group);
  const negative = rounded < 0 && Number(Math.abs(rounded).toFixed(decimals)) !== 0;
  return `${negative ? "-" : ""}${grouped}${fraction === undefined ? "" : settings.decimalSeparator + fraction}`;
}

export function currencyLabel(currencyCode: string): string {
  const currency = currencyCode.toUpperCase();
  return currency === "UNKNOWN" ? "[valuta nepoznata]" : currency === "BAM" || currency === "KM" ? "KM" : currency;
}

export function formatMoney(amount: number, currencyCode = "KM", settings: DisplaySettings = current): string {
  return `${formatNumber(amount, 2, settings)} ${currencyLabel(currencyCode)}`;
}

function parts(value: string): { year: string; month: string; day: string; time: string | null } | null {
  // A plain date ("2026-10-31") is a calendar date; a timestamp is shown in the viewer's local time.
  const plain = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (plain) return { year: plain[1], month: plain[2], day: plain[3], time: null };
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  const pad = (number: number) => String(number).padStart(2, "0");
  return { year: String(date.getFullYear()), month: pad(date.getMonth() + 1), day: pad(date.getDate()),
    time: `${pad(date.getHours())}:${pad(date.getMinutes())}` };
}

export function formatDate(value: string | null | undefined, settings: DisplaySettings = current): string {
  if (!value) return "";
  const date = /^\d{4}-\d{2}-\d{2}T/.test(value) && !/[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value.slice(0, 10) : value;
  const part = parts(date);
  if (!part) return value;
  return settings.dateFormat === "YYYY-MM-DD" ? `${part.year}-${part.month}-${part.day}` : `${part.day}.${part.month}.${part.year}`;
}

export function formatDateTime(value: string | null | undefined, settings: DisplaySettings = current): string {
  if (!value) return "";
  const part = parts(value);
  if (!part) return value;
  return `${formatDate(`${part.year}-${part.month}-${part.day}`, settings)}${part.time ? ` ${part.time}` : ""}`;
}
