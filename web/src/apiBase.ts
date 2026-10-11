// API origin. Empty by default: requests go to /api on the page's own origin, and nginx (Docker), the Vite dev
// proxy and Caddy (production) forward them to the API. The app then also works when opened from another device
// (a phone via the computer's LAN IP). Set VITE_API_URL only for an API on a separate host.
export const apiBase: string = (import.meta as { env?: Record<string, string | undefined> }).env?.VITE_API_URL ?? "";
