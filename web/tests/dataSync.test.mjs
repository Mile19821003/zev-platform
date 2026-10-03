import assert from "node:assert/strict";
import test from "node:test";
import { createApiClient, ApiError, errorText } from "../src/apiClient.ts";
import { refreshVisibleData, resourceKey, subscribeChanges } from "../src/dataSync.ts";
import { updateDependentForm } from "../src/formDependencies.ts";

test("billing preview shows concrete server detail", async () => {
  const request = createApiClient("http://local", async () =>
    Response.json({ failure: 3, detail: "Property unit A-02 has no active owner for selected period." },
      { status: 400 }));
  await assert.rejects(request("/api/zevs/1/finance/billing-runs/preview", { method: "POST" }), (error) => {
    assert.ok(error instanceof ApiError);
    assert.equal(errorText(error), "Property unit A-02 has no active owner for selected period.");
    return true;
  });
});

function fixture() {
  const records = new Map();
  const requests = [];
  let nextId = 1;
  const fetcher = async (url, options) => {
    const path = new URL(url).pathname;
    const key = resourceKey(path);
    const method = options.method ?? "GET";
    requests.push({ path, method });
    const collection = records.get(key) ?? [];
    if (method === "GET") return Response.json({ page: 1, pageSize: 100, totalCount: collection.length, items: collection });
    if (method === "POST") {
      const created = { ...JSON.parse(options.body), id: String(nextId++), isActive: true };
      records.set(key, [...collection, created]);
      return Response.json(created, { status: 201 });
    }
    const id = path.split("/").at(-1) === "status" ? path.split("/").at(-2) : path.split("/").at(-1);
    const changed = JSON.parse(options.body);
    const updated = collection.map((item) => item.id === id ? { ...item, ...changed } : item);
    records.set(key, updated);
    return Response.json(updated.find((item) => item.id === id));
  };
  const request = createApiClient("http://local", fetcher);
  return { request, requests, records };
}

async function observedList(request, path, mutation) {
  const snapshots = [];
  const reads = [];
  const read = () => reads.push(request(path).then((page) => snapshots.push(page.items)));
  const unsubscribe = subscribeChanges([resourceKey(path)], read);
  try {
    await mutation();
    await Promise.all(reads);
    return snapshots.at(-1);
  } finally { unsubscribe(); }
}

test("1 create Organization refreshes list without reload", async () => {
  const { request } = fixture();
  const items = await observedList(request, "/api/system/organizations", () =>
    request("/api/system/organizations", { method: "POST", body: JSON.stringify({ name: "TEST REFRESH ORG" }) }));
  assert.equal(items[0].name, "TEST REFRESH ORG");
});

test("2 create ZEV refreshes Building parent lookup", async () => {
  const { request } = fixture();
  const items = await observedList(request, "/api/system/zevs?organizationId=1", () =>
    request("/api/system/zevs", { method: "POST", body: JSON.stringify({ name: "TEST REFRESH ZEV" }) }));
  assert.equal(items[0].name, "TEST REFRESH ZEV");
});

test("3 create Building refreshes Entrance parent lookup", async () => {
  const { request } = fixture();
  const items = await observedList(request, "/api/system/buildings", () =>
    request("/api/system/buildings", { method: "POST", body: JSON.stringify({ name: "TEST REFRESH BUILDING" }) }));
  assert.equal(items[0].name, "TEST REFRESH BUILDING");
});

test("4 create Entrance refreshes PropertyUnit parent lookup", async () => {
  const { request } = fixture();
  const items = await observedList(request, "/api/system/entrances", () =>
    request("/api/system/entrances", { method: "POST", body: JSON.stringify({ name: "TEST REFRESH ENTRANCE" }) }));
  assert.equal(items[0].name, "TEST REFRESH ENTRANCE");
});

test("5 create PropertyUnit refreshes list", async () => {
  const { request } = fixture();
  const items = await observedList(request, "/api/system/property-units", () =>
    request("/api/system/property-units", { method: "POST", body: JSON.stringify({ code: "A-1" }) }));
  assert.equal(items[0].code, "A-1");
});

test("6 edit immediately invalidates row data", async () => {
  const { request } = fixture();
  const created = await request("/api/system/buildings", { method: "POST", body: JSON.stringify({ name: "Old" }) });
  const items = await observedList(request, "/api/system/buildings", () =>
    request(`/api/system/buildings/${created.id}`, { method: "PUT", body: JSON.stringify({ name: "New" }) }));
  assert.equal(items[0].name, "New");
});

test("7 deactivate immediately invalidates status", async () => {
  const { request } = fixture();
  const created = await request("/api/system/organizations", { method: "POST", body: JSON.stringify({ name: "A" }) });
  const items = await observedList(request, "/api/system/organizations", () =>
    request(`/api/system/organizations/${created.id}/status`, { method: "PATCH", body: JSON.stringify({ isActive: false }) }));
  assert.equal(items[0].isActive, false);
});

test("8 204, empty 200, and non-JSON 202 are successful", async () => {
  for (const status of [204, 200, 202]) {
    let changed = 0;
    const client = createApiClient("http://local", async () => new Response(status === 202 ? "Accepted" : null,
      { status, headers: status === 202 ? { "content-type": "text/plain" } : {} }));
    const unsubscribe = subscribeChanges(["organizations"], () => changed++);
    assert.equal(await client("/api/system/organizations", { method: "POST" }), undefined);
    assert.equal(changed, 1);
    unsubscribe();
  }
});

test("9 400/409 retain failure without success invalidation and expose ProblemDetails", async () => {
  for (const status of [400, 409]) {
    let changed = 0;
    const client = createApiClient("http://local", async () => Response.json({ title: "Greška", detail: "Stvarna backend poruka" }, { status }));
    const unsubscribe = subscribeChanges(["organizations"], () => changed++);
    await assert.rejects(client("/api/system/organizations", { method: "POST" }), (error) => {
      assert.ok(error instanceof ApiError);
      assert.equal(error.status, status);
      assert.equal(errorText(error), "Stvarna backend poruka");
      return true;
    });
    assert.equal(changed, 0);
    unsubscribe();
  }
});

test("10 duplicate in-flight submit sends one request", async () => {
  let requests = 0;
  let release;
  const wait = new Promise((resolve) => { release = resolve; });
  const client = createApiClient("http://local", async () => { requests++; await wait; return Response.json({ id: "1" }, { status: 201 }); });
  const options = { method: "POST", body: JSON.stringify({ name: "Only one" }) };
  const first = client("/api/system/organizations", options);
  const second = client("/api/system/organizations", options);
  release();
  await Promise.all([first, second]);
  assert.equal(requests, 1);
});

test("mutation keys are tenant-scoped for finance and report invalidation", async () => {
  const { request } = fixture();
  let own = 0;
  let other = 0;
  const unsubOwn = subscribeChanges(["zev:one:reports"], () => own++);
  const unsubOther = subscribeChanges(["zev:two:reports"], () => other++);
  await request("/api/zevs/one/finance/billing-runs", { method: "POST", body: "{}" });
  assert.equal(own, 1);
  assert.equal(other, 0);
  unsubOwn(); unsubOther();
});

test("ownership, verified link, override and membership changes refresh access without F5", async () => {
  const { request } = fixture();
  let revisions = 0;
  const unsubscribe = subscribeChanges(["access"], () => revisions++);
  await request("/api/zevs/one/registry/ownerships", { method: "POST", body: "{}" });
  await request("/api/system/people/person/user-link/status", { method: "POST", body: "{}" });
  await request("/api/zevs/one/access/overrides", { method: "POST", body: "{}" });
  await request("/api/system/memberships", { method: "POST", body: "{}" });
  assert.equal(revisions, 4);
  unsubscribe();
});

test("changing a parent clears every descendant without clearing unrelated fields", () => {
  const fields = [
    { key: "zevId", dependsOn: "organizationId" },
    { key: "buildingId", dependsOn: "zevId" },
    { key: "entranceId", dependsOn: "buildingId" },
    { key: "unitId", dependsOn: "entranceId" },
  ];
  const before = { organizationId: "A", zevId: "Z1", buildingId: "B1", entranceId: "E1", unitId: "U1", code: "A-1" };
  assert.deepEqual(updateDependentForm(before, "organizationId", "B", fields), {
    organizationId: "B", zevId: "", buildingId: "", entranceId: "", unitId: "", code: "A-1",
  });
  assert.equal(updateDependentForm(before, "buildingId", "B2", fields).entranceId, "");
});

test("focus refreshes active subscriptions without a page reload", () => {
  let reports = 0;
  let lookups = 0;
  const stopReports = subscribeChanges(["zev:one:reports"], () => reports++);
  const stopLookups = subscribeChanges(["buildings"], () => lookups++);
  refreshVisibleData();
  assert.equal(reports, 1);
  assert.equal(lookups, 1);
  stopReports(); stopLookups();
});

test("401, 403, 422, 429 and 5xx receive distinct messages", () => {
  const messages = [401, 403, 422, 429, 500].map((status) => errorText(new ApiError(status)));
  assert.equal(new Set(messages).size, messages.length);
});

test("a refresh subscriber failure cannot turn a committed write into a mutation error", async () => {
  const { request } = fixture();
  const originalError = console.error;
  console.error = () => {};
  const unsubscribe = subscribeChanges(["organizations"], () => { throw new Error("render failed"); });
  try {
    const saved = await request("/api/system/organizations", { method: "POST", body: JSON.stringify({ name: "Committed" }) });
    assert.equal(saved.name, "Committed");
  } finally { unsubscribe(); console.error = originalError; }
});
