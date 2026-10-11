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

// Download of a protected file (exports): fetched with the token, saved under the given name.
export async function downloadProtectedFile(path: string, token: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiBase}${path}`, { headers: { Authorization: `Bearer ${token}` }, credentials: "include" });
  if (!response.ok) throw new Error(response.status === 403 ? "Nemate pravo izvoza izvještaja." : "Izvoz nije uspio.");
  const url = URL.createObjectURL(await response.blob());
  const anchor = document.createElement("a");
  anchor.href = url; anchor.download = fileName; anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
