// HTTP client for the mobile app: bearer access token in memory, rotating refresh token in secure storage.
export type MobileTokens = { accessToken: string; refreshToken: string };

export type TokenStore = {
  get(): Promise<string | null>;
  set(refreshToken: string): Promise<void>;
  remove(): Promise<void>;
};

export type ApiClientOptions = {
  baseUrl: string | undefined;
  allowInsecure: boolean;
  tokens: TokenStore;
  fetch?: typeof fetch;
  onAccessToken?: (accessToken: string) => void;
};

export class ApiError extends Error {
  constructor(readonly status: number, message: string) { super(message); this.name = "ApiError"; }
}

// The request never reached the API (no network, DNS, TLS, server down); cached data may be shown instead.
export class NetworkError extends Error {
  constructor(message = "Nema veze sa serverom.") { super(message); this.name = "NetworkError"; }
}

export type ApiClient = ReturnType<typeof createApiClient>;

export function createApiClient(options: ApiClientOptions) {
  const send = options.fetch ?? fetch;
  let refreshInFlight: Promise<string | null> | null = null;

  function url(path: string): string {
    if (!options.baseUrl) throw new Error("Postavite EXPO_PUBLIC_API_URL na adresu API-ja dostupnu telefonu.");
    if (!options.baseUrl.startsWith("https://") && !options.allowInsecure)
      throw new Error("Produkcijski API mora koristiti HTTPS.");
    return `${options.baseUrl.replace(/\/+$/, "")}${path}`;
  }

  async function call(path: string, init: RequestInit): Promise<Response> {
    const target = url(path);
    try { return await send(target, init); }
    catch { throw new NetworkError(); }
  }

  function renewAccessToken(): Promise<string | null> {
    if (refreshInFlight) return refreshInFlight;
    refreshInFlight = (async () => {
      const refreshToken = await options.tokens.get();
      if (!refreshToken) return null;
      const response = await call("/api/auth/mobile/refresh", { method: "POST",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken }) });
      if (!response.ok) {
        // Only a definitive rejection ends the session; a server error keeps the token for a later retry.
        if (response.status === 400 || response.status === 401) await options.tokens.remove();
        return null;
      }
      const next = await response.json() as MobileTokens;
      await options.tokens.set(next.refreshToken);
      options.onAccessToken?.(next.accessToken);
      return next.accessToken;
    })().finally(() => { refreshInFlight = null; });
    return refreshInFlight;
  }

  // Sends the request and retries once with a renewed access token after 401.
  async function authorized(path: string, accessToken: string, init: RequestInit): Promise<Response> {
    const withToken = (token: string): RequestInit => ({ ...init,
      headers: { ...(init.headers as Record<string, string> | undefined), ...(token ? { Authorization: `Bearer ${token}` } : {}) } });
    let response = await call(path, withToken(accessToken));
    if (response.status === 401 && accessToken) {
      const nextToken = await renewAccessToken();
      if (nextToken) response = await call(path, withToken(nextToken));
    }
    return response;
  }

  async function request<T>(path: string, accessToken: string, body?: object): Promise<T> {
    const response = await authorized(path, accessToken, { method: body ? "POST" : "GET",
      headers: body ? { "Content-Type": "application/json" } : {}, body: body ? JSON.stringify(body) : undefined });
    if (!response.ok) throw await toError(response);
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  }

  async function download(path: string, accessToken: string): Promise<Response> {
    const response = await authorized(path, accessToken, { method: "GET" });
    if (!response.ok) throw await toError(response, true);
    return response;
  }

  return { request, download, renewAccessToken };
}

async function toError(response: Response, isFile = false): Promise<ApiError> {
  const problem = await response.json().catch(() => null) as { error?: string; title?: string } | null;
  return new ApiError(response.status, errorMessage(response.status, problem?.error ?? problem?.title, isFile));
}

const knownErrors: Record<string, string> = {
  "Charge is already settled.": "Zaduženje je već izmireno – uplatnica nije potrebna.",
  "Payment slip requires a verified charge currency.": "Uplatnicu nije moguće izdati: valuta zaduženja nije potvrđena.",
};

export function errorMessage(status: number, serverMessage: string | undefined, isFile = false): string {
  if (serverMessage && knownErrors[serverMessage]) return knownErrors[serverMessage];
  if (serverMessage) return serverMessage;
  if (status === 401) return "Sesija je istekla. Prijavite se ponovo.";
  if (status === 403 || status === 404) return isFile ? "Datoteka nije dostupna." : "Podatak nije dostupan.";
  if (status === 409) return isFile ? "Uplatnica trenutno nije dostupna (zaduženje je izmireno ili nema računa fonda)."
    : "Zahtjev nije moguće izvršiti u trenutnom stanju.";
  if (status === 429) return "Previše pokušaja. Pokušajte ponovo za minutu.";
  return `Greška servera (${status}).`;
}
