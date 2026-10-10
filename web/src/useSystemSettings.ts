import { useEffect, useState } from "react";
import { callApi } from "./api";
import { DisplaySettings, defaultDisplaySettings, normalizeDisplaySettings } from "./displaySettings.ts";
import { useChangeRevision } from "./useChangeRevision.ts";

const cacheKey = "zev.displaySettings";

function cached(): DisplaySettings {
  try { return normalizeDisplaySettings(JSON.parse(window.localStorage.getItem(cacheKey) ?? "null")); }
  catch { return defaultDisplaySettings; }
}

// Platform display settings for the signed-in user. Reloaded after any change to /api/system/settings
// (dataSync key "settings") and on focus; the last values are cached per browser to avoid a flash on start.
export function useSystemSettings(token: string | null): DisplaySettings {
  const [settings, setSettings] = useState<DisplaySettings>(cached);
  const revision = useChangeRevision("settings");

  useEffect(() => {
    if (!token) return;
    let current = true;
    callApi<unknown>("/api/system/settings", {}, token)
      .then((result) => {
        if (!current) return;
        const next = normalizeDisplaySettings(result);
        setSettings(next);
        try { window.localStorage.setItem(cacheKey, JSON.stringify(next)); } catch { /* cache is optional */ }
      })
      .catch(() => { /* keep the cached or default settings */ });
    return () => { current = false; };
  }, [token, revision]);

  // Base font size of the whole app (rem-based styles scale with it).
  useEffect(() => { document.documentElement.style.fontSize = settings.fontSize; }, [settings.fontSize]);
  return settings;
}
