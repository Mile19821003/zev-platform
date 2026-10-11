// Workspace navigation: modules grouped into four categories for the sidebar (accordion / icon-only) and the
// dashboard grid. The input list is already filtered by the user's effective permissions, so a category with
// no visible module is left out entirely. Icons are palette keys (iconRegistry.ts); the defaults below can be
// overridden by the platform setting GET /api/system/module-icons (setModuleIcons).
export type NavItem = { key: string; label: string };
export type NavGroupId = "finance" | "registry" | "community" | "admin";
export type NavModule = NavItem & { icon: string };
export type NavGroup = { id: NavGroupId; label: string; icon: string; description: string; items: NavModule[] };

export const homeKey = "overview";
export const categoryKey = (id: NavGroupId) => `category:${id}`;

export const navGroups: Omit<NavGroup, "items">[] = [
  { id: "finance", label: "Finansije i računi", icon: "banknote", description: "Obračun, uplate, rashodi i izvještaji" },
  { id: "registry", label: "Registar i nekretnine", icon: "building-2", description: "Zgrade, posebni dijelovi i vlasnici" },
  { id: "community", label: "Zajednica i održavanje", icon: "users", description: "Kvarovi, dokumenti, obavještenja i glasanja" },
  { id: "admin", label: "Administracija i sistem", icon: "shield-check", description: "Uloge, prava, korisnici i procesi" },
];

// Module key (both the system and the role workspace) → category, default icon, display label and the label used
// on the icon settings screen (where the same module of different workspaces must be told apart).
type ModuleInfo = { group: NavGroupId; icon: string; label?: string; catalog: string };
const modules: Record<string, ModuleInfo> = {
  finance: { group: "finance", icon: "file-text", label: "Obračun i računi", catalog: "Obračun i računi" },
  "owner-finance": { group: "finance", icon: "wallet", catalog: "Moj portal (vlasnik)" },
  payments: { group: "finance", icon: "landmark", catalog: "Uplate i izvodi" },
  "bank-transactions": { group: "finance", icon: "arrow-left-right", catalog: "Bankovne transakcije" },
  expenses: { group: "finance", icon: "receipt", catalog: "Rashodi i dugovanja" },
  reports: { group: "finance", icon: "chart-bar", catalog: "Izvještaji" },
  legal: { group: "finance", icon: "scale", catalog: "Opomene i pravni predmeti" },
  zevs: { group: "registry", icon: "building-2", catalog: "ZEV" },
  buildings: { group: "registry", icon: "building", catalog: "Zgrade" },
  entrances: { group: "registry", icon: "door", catalog: "Ulazi" },
  units: { group: "registry", icon: "door", catalog: "Posebni dijelovi" },
  people: { group: "registry", icon: "user", catalog: "Vlasnici / korisnici" },
  ownership: { group: "registry", icon: "scroll", catalog: "Vlasništvo i korisnici" },
  registry: { group: "registry", icon: "building", catalog: "Vlasnici i nekretnine (upravnik)" },
  maintenance: { group: "community", icon: "wrench", catalog: "Održavanje (upravnik)" },
  "representative-maintenance": { group: "community", icon: "wrench", catalog: "Pregled održavanja (predstavnik)" },
  "my-maintenance": { group: "community", icon: "wrench", catalog: "Moje prijave (vlasnik)" },
  community: { group: "community", icon: "megaphone", catalog: "Zajednica i glasanje (upravnik)" },
  "representative-community": { group: "community", icon: "megaphone", catalog: "Oglasna tabla (predstavnik)" },
  "owner-community": { group: "community", icon: "vote", catalog: "Dokumenti i glasanje (vlasnik)" },
  access: { group: "admin", icon: "key", label: "Uloge i matrica prava", catalog: "Uloge i matrica prava (ZEV)" },
  memberships: { group: "admin", icon: "key", label: "Uloge i matrica prava", catalog: "Uloge i matrica prava (sistem)" },
  users: { group: "admin", icon: "users", label: "Korisnici", catalog: "Korisnici" },
  accountLinks: { group: "admin", icon: "link", label: "Veze naloga", catalog: "Veze naloga" },
  organizations: { group: "admin", icon: "network", catalog: "Organizacije / upravnici" },
  hodogram: { group: "admin", icon: "workflow", catalog: "Hodogram" },
  settings: { group: "admin", icon: "sliders", catalog: "Sistemska podešavanja" },
  lookups: { group: "admin", icon: "barcode", catalog: "Šifarnici" },
  icons: { group: "admin", icon: "palette", catalog: "Prikazi i ikone" },
  themes: { group: "admin", icon: "monitor", catalog: "Teme i paleta boja" },
  errors: { group: "admin", icon: "activity", catalog: "Greške u sistemu" },
  versions: { group: "admin", icon: "clipboard-check", catalog: "Istorija verzija" },
  deleted: { group: "admin", icon: "package", catalog: "Obrisani redovi" },
};

// Icons chosen by the system administrator (moduleKey or "category:<id>" → palette key).
let iconOverrides: Record<string, string> = {};
export function setModuleIcons(icons: Record<string, string>): void { iconOverrides = icons; }

export function defaultIcon(moduleKey: string): string {
  if (moduleKey === homeKey) return "home";
  const group = navGroups.find((item) => categoryKey(item.id) === moduleKey);
  return group?.icon ?? modules[moduleKey]?.icon ?? "list";
}

export function iconFor(moduleKey: string, overrides: Record<string, string> = iconOverrides): string {
  return overrides[moduleKey] ?? defaultIcon(moduleKey);
}

// Modules shown as their own card on the home grid as well (quick access), when the user may open them.
export const featuredModuleKeys = ["lookups"];

export function featuredModules(items: NavItem[]): NavModule[] {
  return featuredModuleKeys.flatMap((key) => {
    const item = items.find((entry) => entry.key === key);
    return item ? [{ key: item.key, label: moduleLabel(item), icon: iconFor(key) }] : [];
  });
}

export function moduleLabel(item: NavItem): string {
  return modules[item.key]?.label ?? item.label;
}

export function groupOf(key: string): NavGroupId | null {
  return key === homeKey ? null : modules[key]?.group ?? "admin";
}

// Groups in a fixed order, modules in the order of the input; unknown keys fall back to administration.
export function groupNavigation(items: NavItem[], overrides: Record<string, string> = iconOverrides): NavGroup[] {
  return navGroups.map((group) => ({
    ...group,
    icon: iconFor(categoryKey(group.id), overrides),
    items: items.filter((item) => item.key !== homeKey && groupOf(item.key) === group.id)
      .map((item) => ({ key: item.key, label: moduleLabel(item), icon: iconFor(item.key, overrides) })),
  })).filter((group) => group.items.length > 0);
}

// Every configurable entry for the icon settings screen: the home item, the four categories and all modules.
export type IconCatalogEntry = { moduleKey: string; category: string; label: string; defaultIcon: string };
export function iconCatalog(): IconCatalogEntry[] {
  return [
    { moduleKey: homeKey, category: "Početna", label: "Pregled", defaultIcon: "home" },
    ...navGroups.map((group) => ({ moduleKey: categoryKey(group.id), category: group.label, label: "Kartica kategorije",
      defaultIcon: group.icon })),
    ...navGroups.flatMap((group) => Object.entries(modules).filter(([, info]) => info.group === group.id)
      .map(([key, info]) => ({ moduleKey: key, category: group.label, label: info.catalog, defaultIcon: info.icon }))),
  ];
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
