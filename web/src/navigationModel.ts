// Workspace navigation: modules grouped into four categories for the sidebar (accordion / icon-only) and the
// dashboard grid. The input list is already filtered by the user's effective permissions, so a category with
// no visible module is left out entirely.
export type NavItem = { key: string; label: string };
export type NavGroupId = "finance" | "registry" | "community" | "admin";
export type IconName = "home" | "finance" | "registry" | "community" | "admin" | "invoice" | "wallet" | "bank"
  | "transfer" | "receipt" | "chart" | "scale" | "building" | "door" | "person" | "deed" | "wrench" | "megaphone"
  | "ballot" | "key" | "users" | "link" | "flow" | "organization" | "settings";
export type NavModule = NavItem & { icon: IconName };
export type NavGroup = { id: NavGroupId; label: string; icon: IconName; description: string; items: NavModule[] };

export const homeKey = "overview";

export const navGroups: Omit<NavGroup, "items">[] = [
  { id: "finance", label: "Finansije i računi", icon: "finance", description: "Obračun, uplate, rashodi i izvještaji" },
  { id: "registry", label: "Registar i nekretnine", icon: "registry", description: "Zgrade, posebni dijelovi i vlasnici" },
  { id: "community", label: "Zajednica i održavanje", icon: "community", description: "Kvarovi, dokumenti, obavještenja i glasanja" },
  { id: "admin", label: "Administracija i sistem", icon: "admin", description: "Uloge, prava, korisnici i procesi" },
];

// Module key (both the system and the role workspace) → category and icon.
const modules: Record<string, { group: NavGroupId; icon: IconName; label?: string }> = {
  finance: { group: "finance", icon: "invoice", label: "Obračun i računi" },
  "owner-finance": { group: "finance", icon: "wallet" },
  payments: { group: "finance", icon: "bank" },
  "bank-transactions": { group: "finance", icon: "transfer" },
  expenses: { group: "finance", icon: "receipt" },
  reports: { group: "finance", icon: "chart" },
  legal: { group: "finance", icon: "scale" },
  zevs: { group: "registry", icon: "registry" },
  buildings: { group: "registry", icon: "building" },
  entrances: { group: "registry", icon: "door" },
  units: { group: "registry", icon: "door" },
  people: { group: "registry", icon: "person" },
  ownership: { group: "registry", icon: "deed" },
  registry: { group: "registry", icon: "building" },
  maintenance: { group: "community", icon: "wrench" },
  "representative-maintenance": { group: "community", icon: "wrench" },
  "my-maintenance": { group: "community", icon: "wrench" },
  community: { group: "community", icon: "megaphone" },
  "representative-community": { group: "community", icon: "megaphone" },
  "owner-community": { group: "community", icon: "ballot" },
  access: { group: "admin", icon: "key", label: "Uloge i matrica prava" },
  memberships: { group: "admin", icon: "key", label: "Uloge i matrica prava" },
  users: { group: "admin", icon: "users", label: "Korisnici" },
  accountLinks: { group: "admin", icon: "link", label: "Veze naloga" },
  organizations: { group: "admin", icon: "organization" },
  hodogram: { group: "admin", icon: "flow" },
  settings: { group: "admin", icon: "settings" },
};

export function moduleLabel(item: NavItem): string {
  return modules[item.key]?.label ?? item.label;
}

export function groupOf(key: string): NavGroupId | null {
  return key === homeKey ? null : modules[key]?.group ?? "admin";
}

// Groups in a fixed order, modules in the order of the input; unknown keys fall back to administration.
export function groupNavigation(items: NavItem[]): NavGroup[] {
  return navGroups.map((group) => ({
    ...group,
    items: items.filter((item) => item.key !== homeKey && groupOf(item.key) === group.id)
      .map((item) => ({ key: item.key, label: moduleLabel(item), icon: modules[item.key]?.icon ?? group.icon })),
  })).filter((group) => group.items.length > 0);
}

// Sidebar state remembered per browser. Storage may be missing or throw (private mode); defaults then apply.
export type SidebarState = { collapsed: boolean; expanded: NavGroupId[] };
type KeyValueStorage = Pick<Storage, "getItem" | "setItem">;
const collapsedKey = "zev.nav.collapsed";
const expandedKey = "zev.nav.expanded";
const groupIds = new Set<string>(navGroups.map((group) => group.id));

export function loadSidebarState(storage: KeyValueStorage | undefined): SidebarState {
  try {
    const expanded = JSON.parse(storage?.getItem(expandedKey) ?? "[]");
    return { collapsed: storage?.getItem(collapsedKey) === "1",
      expanded: Array.isArray(expanded) ? expanded.filter((id): id is NavGroupId => groupIds.has(id)) : [] };
  } catch { return { collapsed: false, expanded: [] }; }
}

export function saveSidebarState(storage: KeyValueStorage | undefined, state: SidebarState): void {
  try {
    storage?.setItem(collapsedKey, state.collapsed ? "1" : "0");
    storage?.setItem(expandedKey, JSON.stringify(state.expanded));
  } catch { /* remembering the layout is a convenience only */ }
}

// 24×24 stroke icons (path data), drawn by NavIcon.
export const iconPaths: Record<IconName, string> = {
  home: "M3 11 12 4l9 7M5 10v10h5v-6h4v6h5V10",
  finance: "M3 7h18v10H3zM7 7v10M17 7v10M12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5",
  registry: "M4 21V6l8-3 8 3v15M9 21v-4h6v4M8 8h2M14 8h2M8 12h2M14 12h2",
  community: "M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM16 11a2.5 2.5 0 1 0 0-5M2.5 20c0-3 2.5-5.5 5.5-5.5s5.5 2.5 5.5 5.5M15 14.5c3 0 6 2 6 5.5",
  admin: "M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6zM9 12l2 2 4-4",
  invoice: "M6 3h9l3 3v15H6zM9 9h6M9 13h6M9 17h4",
  wallet: "M4 7h15a1 1 0 0 1 1 1v11H4zM4 7l11-3v3M16 13h2",
  bank: "M3 10h18L12 4zM5 10v8M9.5 10v8M14.5 10v8M19 10v8M3 20h18",
  transfer: "M4 8h14l-3-3M20 16H6l3 3",
  receipt: "M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6",
  chart: "M4 20V4M4 20h16M8 16v-5M12 16V8M16 16v-3",
  scale: "M12 4v16M7 20h10M5 7h14M7 7l-3 6h6zM17 7l-3 6h6z",
  building: "M5 21V4h10v17M15 9h4v12M8 8h2M8 12h2M8 16h2M3 21h18",
  door: "M6 21V4h12v17M3 21h18M14 12h.01",
  person: "M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21c0-4 3.6-7 8-7s8 3 8 7",
  deed: "M6 3h12v18H6zM9 8h6M9 12h6M14 17l1.5 1.5L18 16",
  wrench: "M14.5 6.5a4 4 0 0 0 5 5L12 19a2 2 0 0 1-3-3l7.5-7.5a4 4 0 0 0-2-2zM5 21l2-2",
  megaphone: "M3 10v4h3l7 4V6L6 10zM17 9a4 4 0 0 1 0 6",
  ballot: "M4 12h16v9H4zM8 12V4h8v8M10 8l1.5 1.5L14 7",
  key: "M8 15a4 4 0 1 1 3.5-6H21v3h-2v3h-3v-3h-4.5A4 4 0 0 1 8 15z",
  users: "M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM2 20c0-3.5 3-6 7-6s7 2.5 7 6M17 4.5a3.5 3.5 0 0 1 0 6.5M22 20c0-2.8-1.8-4.8-4.5-5.6",
  link: "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1",
  flow: "M4 4h6v5H4zM14 15h6v5h-6zM7 9v4h10v2",
  organization: "M9 3h6v5H9zM12 8v4M5 12h14M5 12v4M19 12v4M2 16h6v5H2zM16 16h6v5h-6z",
  settings: "M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0M14 4v4M8 10v4M16 16v4",
};
