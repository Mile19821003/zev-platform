type ChangeListener = () => void;

const listeners = new Map<string, Set<ChangeListener>>();

export function subscribeChanges(keys: string[], listener: ChangeListener): () => void {
  for (const key of keys) {
    if (!listeners.has(key)) listeners.set(key, new Set());
    listeners.get(key)!.add(listener);
  }
  return () => {
    for (const key of keys) {
      const group = listeners.get(key);
      group?.delete(listener);
      if (group?.size === 0) listeners.delete(key);
    }
  };
}

export function resourceKey(path: string): string | null {
  const parts = new URL(path, "http://local").pathname.split("/").filter(Boolean);
  if (parts[0] !== "api") return null;
  if (parts[1] === "system") return parts[2] === "property-units" ? "propertyUnits" : parts[2] ?? null;
  if (parts[1] === "admin") return parts[2] ?? null;
  if (parts[1] === "zevs") {
    if (parts[3] === "registry") return parts[4] ?? null;
    if (parts[3] === "image") return "zevs";
    return parts[3] ?? null;
  }
  return null;
}

export function mutationKeys(path: string): string[] {
  const parts = new URL(path, "http://local").pathname.split("/").filter(Boolean);
  const key = resourceKey(path);
  if (!key) return [];
  const keys = new Set([key]);
  if (parts[1] === "system") {
    if (key === "ownerships" || key === "occupancies") keys.add("propertyUnits");
    if (["memberships", "users", "people", "ownerships", "occupancies"].includes(key)) keys.add("access");
    if (["organizations", "zevs", "buildings", "entrances", "propertyUnits"].includes(key)) keys.add("overview");
  }
  if (parts[1] === "zevs") {
    const scope = `zev:${parts[2]}`;
    keys.add(`${scope}:${key}`);
    if (["access", "ownerships", "people"].includes(key)) keys.add("access");
    if (["finance", "payments", "expenses", "legal", "registry", "community", "maintenance"].includes(key)) {
      keys.add(`${scope}:reports`);
      keys.add(`${scope}:owner`);
    }
    if (key === "payments" || key === "expenses") keys.add(`${scope}:finance`);
    if (key === "finance") keys.add(`${scope}:payments`);
  }
  return [...keys];
}

function notify(keys: string[]): void {
  const notified = new Set<ChangeListener>();
  for (const key of keys) {
    for (const listener of listeners.get(key) ?? []) notified.add(listener);
  }
  for (const listener of notified) {
    try { listener(); }
    catch (error) { console.error("Data refresh listener failed", error); }
  }
}

export function publishChange(path: string): void {
  notify(mutationKeys(path));
}

export function refreshVisibleData(): void {
  notify([...listeners.keys()]);
}
