// Read-only offline snapshot (decision #28): the last successfully loaded profile, ZEV list and finance
// cards, shown with their timestamp when the API cannot be reached. Never used to send anything.
import type { OwnerCard } from "./finance";

export type CachedZev = { id: string; code: string; name: string };
export type OfflineSnapshot = {
  version: 1;
  email: string;
  zevs: CachedZev[];
  finance: Record<string, { savedAt: string; cards: OwnerCard[] }>;
  savedAt: string;
};

export type SnapshotStorage = {
  read(): Promise<string | null>;
  write(content: string): Promise<void>;
  remove(): Promise<void>;
};

export function createOfflineCache(storage: SnapshotStorage, now: () => Date = () => new Date()) {
  async function load(): Promise<OfflineSnapshot | null> {
    try {
      const content = await storage.read();
      if (!content) return null;
      const snapshot = JSON.parse(content) as OfflineSnapshot;
      return snapshot?.version === 1 && Array.isArray(snapshot.zevs) && typeof snapshot.finance === "object"
        ? snapshot : null;
    } catch { return null; }
  }

  async function save(update: (current: OfflineSnapshot) => OfflineSnapshot): Promise<void> {
    const current = await load() ?? { version: 1, email: "", zevs: [], finance: {}, savedAt: now().toISOString() };
    try { await storage.write(JSON.stringify({ ...update(current), savedAt: now().toISOString() })); }
    catch { /* The cache is best effort; online use must not fail because of it. */ }
  }

  return {
    load,
    // A different account on the same device starts with an empty snapshot.
    saveProfile: (email: string, zevs: CachedZev[]) => save((current) => ({ ...current, email, zevs,
      finance: current.email === email ? Object.fromEntries(Object.entries(current.finance)
        .filter(([zevId]) => zevs.some((zev) => zev.id === zevId))) : {} })),
    saveFinance: (zevId: string, cards: OwnerCard[]) => save((current) => ({ ...current,
      finance: { ...current.finance, [zevId]: { savedAt: now().toISOString(), cards } } })),
    clear: async () => { try { await storage.remove(); } catch { /* already gone */ } },
  };
}

export type OfflineCache = ReturnType<typeof createOfflineCache>;
