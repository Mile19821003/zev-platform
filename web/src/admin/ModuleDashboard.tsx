import { useState } from "react";
import { groupNavigation, NavGroupId, NavItem } from "../navigationModel.ts";
import { NavIcon } from "./WorkspaceNav";

// Home grid: one large card per category; a category opens its module cards with a breadcrumb back.
// Only modules the user may open are passed in, so categories follow the effective permissions.
export default function ModuleDashboard({ items, onSelect }: { items: NavItem[]; onSelect: (key: string) => void }) {
  const [category, setCategory] = useState<NavGroupId | null>(null);
  const groups = groupNavigation(items);
  const selected = groups.find((group) => group.id === category);

  if (!selected) return <section className="module-dashboard" aria-label="Kontrolna ploča">
    <h2>Moduli</h2>
    {groups.length === 0 ? <p className="empty-inline">Za ovaj ZEV nemate dodijeljenih modula.</p>
      : <div className="module-grid">{groups.map((group) => <button type="button" key={group.id}
        className="module-card category" onClick={() => setCategory(group.id)}>
        <span className="module-icon"><NavIcon name={group.icon} size={30} /></span>
        <strong>{group.label}</strong><span className="module-description">{group.description}</span>
        <span className="module-count">{group.items.length === 1 ? "1 modul" : `${group.items.length} modula`}:
          {" "}{group.items.map((module) => module.label).join(", ")}</span>
      </button>)}</div>}
  </section>;

  return <section className="module-dashboard" aria-label={`Kontrolna ploča · ${selected.label}`}>
    <nav className="breadcrumb" aria-label="Putanja">
      <button type="button" className="breadcrumb-back" onClick={() => setCategory(null)}>← Nazad</button>
      <button type="button" className="breadcrumb-home" onClick={() => setCategory(null)}>
        <NavIcon name="home" size={16} /> Meni</button>
      <span aria-hidden="true">›</span><span aria-current="page">{selected.label}</span>
    </nav>
    <div className="module-grid">{selected.items.map((module) => <button type="button" key={module.key}
      className="module-card" onClick={() => onSelect(module.key)}>
      <span className="module-icon"><NavIcon name={module.icon} size={28} /></span>
      <strong>{module.label}</strong></button>)}</div>
  </section>;
}
