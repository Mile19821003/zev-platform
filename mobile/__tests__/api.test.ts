import { ApiError, createApiClient, errorMessage, NetworkError, type TokenStore } from "../src/api";

type Call = { url: string; init: RequestInit };

function json(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body),
    { status, headers: { "Content-Type": "application/json" } });
}

function setup(responses: Array<Response | Error>, refreshToken: string | null = "refresh-1") {
  const calls: Call[] = [];
  let stored = refreshToken;
  const tokens: TokenStore = {
    get: async () => stored,
    set: async (value) => { stored = value; },
    remove: async () => { stored = null; },
  };
  const renewed: string[] = [];
  const fetchMock = jest.fn(async (url: string, init: RequestInit) => {
    calls.push({ url, init });
    const next = responses.shift();
    if (!next) throw new Error("unexpected request");
    if (next instanceof Error) throw next;
    return next;
  });
  const client = createApiClient({ baseUrl: "https://zev.example.com/", allowInsecure: false, tokens,
    fetch: fetchMock as unknown as typeof fetch, onAccessToken: (token) => renewed.push(token) });
  return { client, calls, renewed, stored: () => stored };
}

describe("api client", () => {
  it("sends bearer token and JSON body", async () => {
    const { client, calls } = setup([json(200, { id: "t1" })]);
    await expect(client.request("/api/zevs/z/files", "access-1", { a: 1 })).resolves.toEqual({ id: "t1" });
    expect(calls[0].url).toBe("https://zev.example.com/api/zevs/z/files");
    expect(calls[0].init.method).toBe("POST");
    expect(calls[0].init.headers).toEqual({ "Content-Type": "application/json", Authorization: "Bearer access-1" });
    expect(calls[0].init.body).toBe('{"a":1}');
  });

  it("renews the access token once after 401, rotates the refresh token and retries", async () => {
    const { client, calls, renewed, stored } = setup([json(401),
      json(200, { accessToken: "access-2", refreshToken: "refresh-2" }), json(200, [{ ok: true }])]);
    await expect(client.request("/api/me/property-units", "access-1")).resolves.toEqual([{ ok: true }]);
    expect(calls.map((call) => call.url)).toEqual(["https://zev.example.com/api/me/property-units",
      "https://zev.example.com/api/auth/mobile/refresh", "https://zev.example.com/api/me/property-units"]);
    expect(calls[1].init.body).toBe('{"refreshToken":"refresh-1"}');
    expect(calls[2].init.headers).toEqual({ Authorization: "Bearer access-2" });
    expect(renewed).toEqual(["access-2"]);
    expect(stored()).toBe("refresh-2");
  });

  it("shares one refresh between parallel requests", async () => {
    const { client, calls } = setup([json(200, { accessToken: "a", refreshToken: "r" })]);
    const [first, second] = await Promise.all([client.renewAccessToken(), client.renewAccessToken()]);
    expect(first).toBe("a"); expect(second).toBe("a");
    expect(calls).toHaveLength(1);
  });

  it("forgets a rejected refresh token but keeps it after a server error", async () => {
    const rejected = setup([json(401)]);
    await expect(rejected.client.renewAccessToken()).resolves.toBeNull();
    expect(rejected.stored()).toBeNull();
    const failing = setup([json(503)]);
    await expect(failing.client.renewAccessToken()).resolves.toBeNull();
    expect(failing.stored()).toBe("refresh-1");
  });

  it("reports an unreachable server as NetworkError and keeps the refresh token", async () => {
    const { client, stored } = setup([new TypeError("Network request failed")]);
    await expect(client.renewAccessToken()).rejects.toBeInstanceOf(NetworkError);
    expect(stored()).toBe("refresh-1");
  });

  it("maps API errors to Bosnian messages", async () => {
    const { client } = setup([json(409, { error: "Charge is already settled." }), json(404)]);
    await expect(client.download("/api/zevs/z/charges/c/payment-slip.pdf", "t"))
      .rejects.toEqual(new ApiError(409, "Zaduženje je već izmireno – uplatnica nije potrebna."));
    await expect(client.request("/api/me/finance/z", "t")).rejects.toMatchObject({ status: 404,
      message: "Podatak nije dostupan." });
    expect(errorMessage(429, undefined)).toBe("Previše pokušaja. Pokušajte ponovo za minutu.");
    expect(errorMessage(400, "Lozinke se ne podudaraju.")).toBe("Lozinke se ne podudaraju.");
  });

  it("refuses plain HTTP outside development and a missing API address", async () => {
    const tokens: TokenStore = { get: async () => null, set: async () => {}, remove: async () => {} };
    const fetchMock = jest.fn();
    const insecure = createApiClient({ baseUrl: "http://192.168.1.5:8080", allowInsecure: false, tokens,
      fetch: fetchMock as unknown as typeof fetch });
    await expect(insecure.request("/api/me", "t")).rejects.toThrow("Produkcijski API mora koristiti HTTPS.");
    const missing = createApiClient({ baseUrl: undefined, allowInsecure: true, tokens,
      fetch: fetchMock as unknown as typeof fetch });
    await expect(missing.request("/api/me", "t")).rejects.toThrow("EXPO_PUBLIC_API_URL");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returns undefined for 204", async () => {
    const { client } = setup([new Response(null, { status: 204 })]);
    await expect(client.request("/api/auth/mobile/logout", "", { refreshToken: "r" })).resolves.toBeUndefined();
  });
});
