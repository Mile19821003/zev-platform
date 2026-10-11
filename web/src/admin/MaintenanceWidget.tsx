import { useEffect, useState } from "react";
import { callApi } from "../api";
import { formatDate } from "../displaySettings.ts";
import { calendarStatusHandoffKey, MaintenanceSummary, summaryText, summaryTone } from "../maintenanceCalendar.ts";
import { useChangeRevision } from "../useChangeRevision.ts";

// Home screen card: due maintenance and work in progress of the active ZEV; a click opens the calendar on "Dospjelo".
export default function MaintenanceWidget({ token, zevId, onOpen }: { token: string; zevId: string; onOpen: () => void }) {
  const [summary, setSummary] = useState<MaintenanceSummary | null>(null);
  const revision = useChangeRevision("maintenance", `zev:${zevId}:maintenance`);
  useEffect(() => {
    let current = true;
    callApi<MaintenanceSummary>(`/api/zevs/${zevId}/maintenance/dashboard-summary`, {}, token)
      .then((result) => { if (current) setSummary(result); }).catch(() => { if (current) setSummary(null); });
    return () => { current = false; };
  }, [token, zevId, revision]);
  if (!summary) return null;
  const tone = summaryTone(summary);
  return <button type="button" className={`maintenance-widget tone-${tone}`} onClick={() => {
    try { window.sessionStorage.setItem(calendarStatusHandoffKey, "Due"); } catch { /* the calendar then opens unfiltered */ }
    onOpen();
  }}>
    <span className={`widget-badge tone-${tone}`}>{tone === "danger" ? "Dospjelo" : tone === "warning" ? "U toku" : "Uredno"}</span>
    <strong>{summaryText(summary)}</strong>
    <span className="widget-detail">{summary.unreadTickets ? `${summary.unreadTickets} novih prijava · ` : ""}
      {summary.nextDueOn ? `sljedeći termin ${formatDate(summary.nextDueOn)}` : "nema zakazanih termina"}</span>
    <span className="widget-link">Otvori kalendar održavanja →</span>
  </button>;
}
