import { apiBase } from "./apiBase.ts";
// ZEV cover photos. The API serves them only to signed-in readers (GET /api/zevs/{id}/image), and an <img> cannot
// send the access token, so photos are fetched with it and shown from blob URLs (cached for the session, per
// versioned URL — a new photo has a new URL).
export const maxImageBytes = 5_000_000;
export const imageTypes: Record<string, string[]> = {
  "image/jpeg": [".jpg", ".jpeg"], "image/png": [".png"], "image/webp": [".webp"],
};
export const imageAccept = ".jpg,.jpeg,.png,.webp";

// Same rules as the API (type, extension, size); the API also checks the file's own signature.
export function validateImageFile(file: { name: string; type: string; size: number }): string | null {
  const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
  if (!imageTypes[file.type]?.includes(extension)) return "Dozvoljene su slike JPG, PNG ili WEBP.";
  if (file.size > maxImageBytes) return "Slika je veća od 5 MB.";
  if (file.size === 0) return "Datoteka je prazna.";
  return null;
}

type Loaded = { src: string | null; failed: boolean };
const cache = new Map<string, Loaded>();
const pending = new Map<string, Promise<Loaded>>();

export function cachedZevImage(url: string | null | undefined): Loaded | undefined {
  return url ? cache.get(url) : undefined;
}

// For tests and for a freshly uploaded file whose preview is already known.
export function primeZevImage(url: string, src: string): void {
  cache.set(url, { src, failed: false });
}


export function loadZevImage(url: string, token: string): Promise<Loaded> {
  const known = cache.get(url);
  if (known) return Promise.resolve(known);
  const running = pending.get(url);
  if (running) return running;
  const request = fetch(`${apiBase}${url}`, { headers: { Authorization: `Bearer ${token}` }, credentials: "include" })
    .then(async (response) => response.ok ? { src: URL.createObjectURL(await response.blob()), failed: false }
      : { src: null, failed: true })
    .catch(() => ({ src: null, failed: true }))
    .then((loaded) => { cache.set(url, loaded); pending.delete(url); return loaded; });
  pending.set(url, request);
  return request;
}

export function readFileBase64(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",", 2)[1] ?? "");
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
