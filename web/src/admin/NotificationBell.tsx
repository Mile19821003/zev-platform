import { useEffect, useState } from "react";
import { callApi } from "../api";
import { bellBadge, unreadLabel } from "../maintenanceTickets.ts";
import { useChangeRevision } from "../useChangeRevision.ts";

// 🔔 in the header: new (unread) fault reports of the active ZEV. Reloads on maintenance changes and every minute.
export default function NotificationBell({ token, zevId, onOpen }: { token: string; zevId: string; onOpen: () => void }) {
  const [unread, setUnread] = useState(0);
  const [tick, setTick] = useState(0);
  const revision = useChangeRevision("maintenance", `zev:${zevId}:maintenance`);

  useEffect(() => {
    const timer = window.setInterval(() => setTick((value) => value + 1), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let current = true;
    callApi<{ unread: number }>(`/api/zevs/${zevId}/maintenance/tickets/unread-count`, {}, token)
      .then((result) => { if (current) setUnread(result.unread); })
      .catch(() => { if (current) setUnread(0); });
    return () => { current = false; };
  }, [token, zevId, revision, tick]);

  const badge = bellBadge(unread);
  return <button type="button" className={`notification-bell${badge ? " has-unread" : ""}`} onClick={onOpen}
    aria-label={unreadLabel(unread)} title={unreadLabel(unread)}>
    <span aria-hidden="true">🔔</span>{badge && <span className="bell-badge" aria-hidden="true">{badge}</span>}
  </button>;
}
