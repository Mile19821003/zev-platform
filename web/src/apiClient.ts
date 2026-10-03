import { publishChange } from "./dataSync.ts";
import type { BillingIssue } from "./billingUi.ts";

type Problem = { title?: string; detail?: string; error?: string;
  errors?: Record<string, string[]>; issues?: BillingIssue[] };

export class ApiError extends Error {
  status: number;
  issues: BillingIssue[];
  constructor(status: number, message?: string, issues: BillingIssue[] = []) {
    super(message || `API request failed (${status})`);
    this.status = status;
    this.issues = issues;
  }
}

function problemMessage(body: unknown): string | undefined {
  if (!body || typeof body !== "object") return undefined;
  const problem = body as Problem;
  const validation = problem.errors && Object.values(problem.errors).flat().find((value) => typeof value === "string");
  return problem.detail || problem.error || validation || problem.title;
}

async function parseBody(response: Response): Promise<unknown> {
  if ([204, 205, 304].includes(response.status) || response.headers.get("content-length") === "0") return undefined;
  const contentType = response.headers.get("content-type")?.toLowerCase() ?? "";
  if (!contentType.includes("json")) return undefined;
  const body = await response.text();
  return body.trim() ? JSON.parse(body) : undefined;
}

const inFlightMutations = new Map<string, Promise<unknown>>();

export function createApiClient(baseUrl: string, fetcher: typeof fetch = fetch) {
  return async function request<T>(path: string, options: RequestInit = {}, accessToken?: string): Promise<T> {
    const method = (options.method ?? "GET").toUpperCase();
    const mutation = !["GET", "HEAD", "OPTIONS"].includes(method);
    const duplicateKey = mutation && (typeof options.body === "string" || !options.body)
      ? JSON.stringify([baseUrl, method, path, accessToken, options.body ?? ""]) : null;
    const existing = duplicateKey && inFlightMutations.get(duplicateKey);
    if (existing) return existing as Promise<T>;

    const pending = (async () => {
      let response: Response;
      try {
        response = await fetcher(`${baseUrl}${path}`, {
          ...options,
          credentials: "include",
          headers: {
            ...(options.body && !(options.body instanceof FormData) ? { "Content-Type": "application/json" } : {}),
            ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
            ...options.headers,
          },
        });
      } catch (error) {
        if (options.signal?.aborted) throw error;
        throw new Error("Mrežna veza nije dostupna ili je zahtjev istekao.", { cause: error });
      }
      let parsed: unknown;
      try { parsed = await parseBody(response); }
      catch (error) {
        if (response.ok) throw new Error("Server je vratio neispravan JSON odgovor.", { cause: error });
      }
      if (!response.ok) {
        const problem = parsed as Problem | undefined;
        throw new ApiError(response.status, problemMessage(parsed), Array.isArray(problem?.issues) ? problem.issues : []);
      }
      if (mutation && !path.startsWith("/api/auth/") && !path.endsWith("/preview")
        && !path.endsWith("/preflight")) publishChange(path);
      return parsed as T;
    })();

    if (duplicateKey) inFlightMutations.set(duplicateKey, pending);
    try { return await pending; }
    finally { if (duplicateKey && inFlightMutations.get(duplicateKey) === pending) inFlightMutations.delete(duplicateKey); }
  };
}

export function errorText(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.message !== `API request failed (${error.status})`) return error.message;
    if (error.status === 400 || error.status === 422) return "Provjerite unesene podatke.";
    if (error.status === 401) return "Sesija je istekla. Prijavite se ponovo.";
    if (error.status === 403) return "Nemate ovlaštenje za ovu radnju.";
    if (error.status === 404) return "Traženi zapis nije pronađen ili više nije dostupan.";
    if (error.status === 409) return "Zapis je u međuvremenu izmijenjen ili već postoji. Osvježite podatke i pokušajte ponovo.";
    if (error.status === 429) return "Previše zahtjeva. Pokušajte ponovo kasnije.";
    if (error.status >= 500) return "Server trenutno ne može obraditi zahtjev. Pokušajte ponovo kasnije.";
  }
  return error instanceof Error ? error.message : "Zahtjev nije uspio. Pokušajte ponovo.";
}
