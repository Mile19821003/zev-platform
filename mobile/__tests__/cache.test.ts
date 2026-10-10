import { createOfflineCache, type SnapshotStorage } from "../src/cache";
import type { OwnerCard } from "../src/finance";

function memoryStorage(initial: string | null = null) {
  let content = initial;
  const storage: SnapshotStorage = {
    read: async () => content,
    write: async (value) => { content = value; },
    remove: async () => { content = null; },
  };
  return { storage, content: () => content };
}

const card: OwnerCard = { personId: "p1", balances: [], events: [], invoices: [], page: 1, pageSize: 100, totalCount: 0 };
const zevs = [{ id: "z1", code: "ZEV-1", name: "Zgrada 1" }, { id: "z2", code: "ZEV-2", name: "Zgrada 2" }];

describe("offline cache", () => {
  it("stores profile, ZEV list and cards with timestamps", async () => {
    let clock = new Date("2026-10-10T08:00:00Z");
    const { storage } = memoryStorage();
    const cache = createOfflineCache(storage, () => clock);
    await cache.saveProfile("vlasnik@example.com", zevs);
    clock = new Date("2026-10-10T08:05:00Z");
    await cache.saveFinance("z1", [card]);
    const snapshot = await cache.load();
    expect(snapshot).toMatchObject({ version: 1, email: "vlasnik@example.com", zevs,
      savedAt: "2026-10-10T08:05:00.000Z" });
    expect(snapshot?.finance.z1).toEqual({ savedAt: "2026-10-10T08:05:00.000Z", cards: [card] });
  });

  it("drops another account's data and ZEVs that are no longer accessible", async () => {
    const { storage } = memoryStorage();
    const cache = createOfflineCache(storage);
    await cache.saveProfile("a@example.com", zevs);
    await cache.saveFinance("z1", [card]);
    await cache.saveFinance("z2", [card]);
    await cache.saveProfile("a@example.com", [zevs[1]]);
    expect(Object.keys((await cache.load())!.finance)).toEqual(["z2"]);
    await cache.saveProfile("b@example.com", zevs);
    expect((await cache.load())!.finance).toEqual({});
  });

  it("ignores corrupt or foreign content and clears on logout", async () => {
    expect(await createOfflineCache(memoryStorage("{nije json").storage).load()).toBeNull();
    expect(await createOfflineCache(memoryStorage('{"version":2}').storage).load()).toBeNull();
    const memory = memoryStorage();
    const cache = createOfflineCache(memory.storage);
    await cache.saveProfile("a@example.com", zevs);
    await cache.clear();
    expect(memory.content()).toBeNull();
    expect(await cache.load()).toBeNull();
  });

  it("never fails the caller when the device storage fails", async () => {
    const cache = createOfflineCache({ read: async () => { throw new Error("io"); },
      write: async () => { throw new Error("disk full"); }, remove: async () => { throw new Error("io"); } });
    await expect(cache.saveFinance("z1", [card])).resolves.toBeUndefined();
    await expect(cache.load()).resolves.toBeNull();
    await expect(cache.clear()).resolves.toBeUndefined();
  });
});
