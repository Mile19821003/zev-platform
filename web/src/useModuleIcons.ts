import { useEffect, useState } from "react";
import { callApi } from "./api";
import { normalizeModuleIcons } from "./iconRegistry.ts";
import { useChangeRevision } from "./useChangeRevision.ts";

const cacheKey = "zev.moduleIcons";

function cached(): Record<string, string> {
  try { return normalizeModuleIcons({ icons: JSON.parse(window.localStorage.getItem(cacheKey) ?? "null") }); }
  catch { return {}; }
}

// Module icon map for the signed-in user; reloaded after any change to /api/system/module-icons (dataSync key
// "module-icons") and cached per browser so the navigation does not flash default icons on start.
export function useModuleIcons(token: string | null): Record<string, string> {
  const [icons, setIcons] = useState<Record<string, string>>(cached);
  const revision = useChangeRevision("module-icons");
  useEffect(() => {
    if (!token) return;
    let current = true;
    callApi<unknown>("/api/system/module-icons", {}, token)
      .then((result) => {
        if (!current) return;
        const next = normalizeModuleIcons(result);
        setIcons(next);
        try { window.localStorage.setItem(cacheKey, JSON.stringify(next)); } catch { /* cache is optional */ }
      })
      .catch(() => { /* keep cached or default icons */ });
    return () => { current = false; };
  }, [token, revision]);
  return icons;
}
