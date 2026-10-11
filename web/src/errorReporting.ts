import { apiBase } from "./apiBase.ts";
// Sends unhandled web app errors to POST /api/system/errors/client. Reports are throttled and deduplicated in the
// browser (the API also rate limits), and reporting itself can never throw or report its own failures.
export type ClientErrorReport = { title: string; errorType: string; message: string; stackTrace: string | null; url: string | null };
type Send = (report: ClientErrorReport) => Promise<unknown>;

export function buildClientReport(error: unknown, title: string, url: string | null, extra?: string | null): ClientErrorReport {
  const value = error instanceof Error ? error : null;
  const message = value?.message || (typeof error === "string" ? error : JSON.stringify(error) ?? "Nepoznata greška");
  const stack = [value?.stack, extra].filter(Boolean).join("\n\n");
  return {
    title: title.slice(0, 200), errorType: value?.name || typeof error, message: message.slice(0, 2000),
    stackTrace: stack ? stack.slice(0, 20000) : null, url: url ? url.slice(0, 300) : null,
  };
}

// At most `limit` reports per minute and each distinct error (type + message) once per page session.
export function createReporter(send: Send, now: () => number = Date.now, limit = 5) {
  const seen = new Set<string>();
  let sent: number[] = [];
  return (report: ClientErrorReport): boolean => {
    const key = `${report.errorType}|${report.message}`;
    const time = now();
    sent = sent.filter((at) => time - at < 60_000);
    if (seen.has(key) || sent.length >= limit) return false;
    seen.add(key); sent.push(time);
    try { void send(report).catch(() => undefined); } catch { /* never let reporting fail the app */ }
    return true;
  };
}

export const reportClientError = createReporter((report) => fetch(`${apiBase}/api/system/errors/client`, {
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(report), keepalive: true,
}));

// Errors outside React rendering (event handlers, timers, rejected promises).
export function installGlobalErrorReporting(target: Window = window): void {
  target.addEventListener("error", (event) => reportClientError(buildClientReport(event.error ?? event.message,
    `Neobrađena greška: ${event.message || "nepoznato"}`, target.location.href)));
  target.addEventListener("unhandledrejection", (event) => reportClientError(buildClientReport(event.reason,
    "Neobrađeno odbijeno obećanje (Promise)", target.location.href)));
}
