import { useEffect, useState } from "react";
import { paletteIcon } from "../iconRegistry.ts";
import { groupNavigation, groupOf, homeKey, iconFor, loadSidebarState, NavItem, saveSidebarState,
  SidebarState } from "../navigationModel.ts";

// Draws a palette icon by key; unknown keys fall back to the list icon.
export function NavIcon({ name, size = 20 }: { name: string; size?: number }) {
  return <svg className="nav-icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
    <path d={paletteIcon(name).path} /></svg>;
}

function browserStorage(): Storage | undefined {
  try { return window.localStorage; } catch { return undefined; }
}

// Expanded / icon-only mode and open accordion groups, remembered in localStorage.
export function useSidebarState(): [SidebarState, (next: SidebarState) => void] {
  const [state, setState] = useState(() => loadSidebarState(browserStorage()));
  return [state, (next) => { setState(next); saveSidebarState(browserStorage(), next); }];
}

// Left sidebar: accordion groups ([+] / [−]) when expanded, a vertical column of icons with tooltips when collapsed.
export default function WorkspaceNav({ label, items, active, onSelect, note, state, onState }: {
  label: string; items: NavItem[]; active: string; onSelect: (key: string) => void; note: string;
  state: SidebarState; onState: (next: SidebarState) => void;
}) {
  const groups = groupNavigation(items);
  const home = items.find((item) => item.key === homeKey);
  const activeGroup = groupOf(active);

  // The group of the open module is always unfolded, so the current page is visible in the menu.
  useEffect(() => {
    if (activeGroup && !state.expanded.includes(activeGroup))
      onState({ ...state, expanded: [...state.expanded, activeGroup] });
  }, [activeGroup]);

  function toggleGroup(id: (typeof groups)[number]["id"]) {
    onState({ ...state, expanded: state.expanded.includes(id)
      ? state.expanded.filter((item) => item !== id) : [...state.expanded, id] });
  }

  const item = (key: string, text: string, icon: string) => <button type="button" key={key}
    className={`nav-item${active === key ? " active" : ""}`} aria-current={active === key ? "page" : undefined}
    title={state.collapsed ? text : undefined} onClick={() => onSelect(key)}>
    <NavIcon name={icon} /><span className="nav-text">{text}</span></button>;

  return <aside className={`sidebar workspace-nav${state.collapsed ? " collapsed" : ""}`}>
    <div className="brand"><span className="brand-mark small">Z</span><span className="nav-text">ZEV platforma</span>
      <button type="button" className="nav-toggle" aria-expanded={!state.collapsed}
        aria-label={state.collapsed ? "Raširi meni" : "Sažmi meni na ikonice"}
        title={state.collapsed ? "Raširi meni" : "Sažmi meni"}
        onClick={() => onState({ ...state, collapsed: !state.collapsed })}>{state.collapsed ? "›" : "‹"}</button></div>
    <nav aria-label={label}>
      {home && item(home.key, home.label, iconFor(homeKey))}
      {groups.map((group) => {
        const open = state.collapsed || state.expanded.includes(group.id);
        return <div className={`nav-group${open ? " open" : ""}`} key={group.id}>
          {!state.collapsed && <button type="button" className="nav-group-header" aria-expanded={open}
            aria-controls={`nav-group-${group.id}`} onClick={() => toggleGroup(group.id)}>
            <NavIcon name={group.icon} /><span className="nav-text">{group.label}</span>
            <span className="nav-sign" aria-hidden="true">{open ? "−" : "+"}</span></button>}
          {open && <div className="nav-group-items" id={`nav-group-${group.id}`} role="group" aria-label={group.label}>
            {group.items.map((module) => item(module.key, module.label, module.icon))}</div>}
        </div>;
      })}
    </nav>
    <div className="sidebar-note nav-text">{note}</div>
  </aside>;
}
