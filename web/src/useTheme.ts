import { useEffect, useState } from "react";
import { callApi } from "./api";
import { applyTheme, factoryColors, normalizeTheme, Theme, ThemeColors } from "./theme.ts";
import { useChangeRevision } from "./useChangeRevision.ts";

const cacheKey = "zev.theme";

function cachedTheme(): Theme {
  try { return normalizeTheme(JSON.parse(window.localStorage.getItem(cacheKey) ?? "null")); }
  catch { return normalizeTheme(null); }
}

// Saved palette and an optional live preview (the theme editor); the preview wins until it is cleared.
let saved: ThemeColors = factoryColors;
let preview: ThemeColors | null = null;
function render() { applyTheme(document.documentElement.style, preview ?? saved); }

// Before the first render (main.tsx), so the last palette is used from the start instead of flashing the default.
export function applyCachedTheme(): void {
  saved = cachedTheme().colors;
  render();
}

export function previewTheme(colors: ThemeColors | null): void {
  preview = colors;
  render();
}

// Loads GET /api/system/theme (also before sign-in) and reloads it after any change (dataSync key "theme").
export function useTheme(token: string | null): Theme {
  const [theme, setTheme] = useState<Theme>(cachedTheme);
  const revision = useChangeRevision("theme");

  useEffect(() => {
    let current = true;
    callApi<unknown>("/api/system/theme", {}, token ?? undefined)
      .then((result) => {
        if (!current) return;
        const next = normalizeTheme(result);
        setTheme(next);
        try { window.localStorage.setItem(cacheKey, JSON.stringify(result)); } catch { /* cache is optional */ }
      })
      .catch(() => { /* keep the cached or factory palette */ });
    return () => { current = false; };
  }, [token, revision]);

  useEffect(() => { saved = theme.colors; render(); }, [theme]);
  return theme;
}
