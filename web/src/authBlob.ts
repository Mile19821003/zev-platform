// Files served only to signed-in users (attachments, photos): fetched with the token and shown from blob URLs,
// cached per path for the session.
const cache = new Map<string, Promise<string | null>>();
const apiBase = (import.meta as { env?: Record<string, string | undefined> }).env?.VITE_API_URL ?? "http://localhost:8080";

export function loadBlobUrl(path: string, token: string): Promise<string | null> {
  const known = cache.get(path);
  if (known) return known;
  const request = fetch(`${apiBase}${path}`, { headers: { Authorization: `Bearer ${token}` }, credentials: "include" })
    .then(async (response) => response.ok ? URL.createObjectURL(await response.blob()) : null)
    .catch(() => null);
  cache.set(path, request);
  void request.then((url) => { if (!url) cache.delete(path); });
  return request;
}

export function forgetBlobUrl(path: string): void {
  cache.delete(path);
}
