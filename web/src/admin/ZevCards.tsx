import type { ReactNode } from "react";
import ZevCover from "./ZevCover";

type CardZev = { id: string; code: string; name: string; addressLine?: string | null; imageUrl?: string | null };

// Community cards on the home screen. The whole card (photo + name) is one button that makes the ZEV active;
// the active one is outlined in the primary colour and marked "Aktivan", so switching is always one click.
export default function ZevCardGrid<T extends CardZev>({ token, zevs, activeId, onSelect, detail }: {
  token: string; zevs: T[]; activeId: string | null; onSelect: (id: string) => void; detail?: (zev: T) => ReactNode;
}) {
  return <div className="zev-grid" role="list" aria-label="Zajednice">{zevs.map((zev) => {
    const active = zev.id === activeId;
    return <div role="listitem" key={zev.id}>
      <button type="button" className={`zev-card selectable${active ? " active" : ""}`} aria-pressed={active}
        aria-label={`${zev.name}${active ? " (aktivni ZEV)" : ", otvori radni prostor"}`} onClick={() => onSelect(zev.id)}>
        <ZevCover token={token} imageUrl={zev.imageUrl} name={zev.name} />
        {active && <span className="zev-active-badge">Aktivan</span>}
        <span className="eyebrow">{zev.code}</span>
        <strong className="zev-card-name">{zev.name}</strong>
        {zev.addressLine && <span className="zev-card-line">{zev.addressLine}</span>}
        {detail && <span className="zev-card-line muted">{detail(zev)}</span>}
      </button>
    </div>;
  })}</div>;
}
