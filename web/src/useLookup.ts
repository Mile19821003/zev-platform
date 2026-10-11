import { useEffect, useState } from "react";
import { callApi } from "./api";
import { subscribeChanges } from "./dataSync.ts";
import { LookupOption, lookupFallbacks, LookupTypeCode, normalizeLookupResponse } from "./lookupOptions.ts";

// Session cache: one request per code list, shared by every form; cleared when an administrator edits code lists
// (dataSync key "lookups" from /api/admin/lookups).
const cache = new Map<string, Promise<LookupOption[] | null>>();
const loaded = new Map<string, LookupOption[]>();
const listeners = new Set<() => void>();
subscribeChanges(["lookups"], () => { cache.clear(); loaded.clear(); listeners.forEach((listener) => listener()); });

export type LookupState = { items: LookupOption[]; source: "api" | "fallback" };

function load(typeCode: LookupTypeCode, token: string): Promise<LookupOption[] | null> {
  let request = cache.get(typeCode);
  if (!request) {
    request = callApi<unknown>(`/api/lookups/${typeCode}`, {}, token).then(normalizeLookupResponse)
      .then((items) => { if (items) loaded.set(typeCode, items); else cache.delete(typeCode); return items; })
      .catch(() => { cache.delete(typeCode); return null; });
    cache.set(typeCode, request);
  }
  return request;
}

// Options of one code list. The fallback (system defaults) is shown immediately, so a slow or failing API never
// blocks a form; the loaded list replaces it as soon as it arrives.
export function useLookup(typeCode: LookupTypeCode, token: string): LookupState {
  const [state, setState] = useState<LookupState>(() => {
    const items = loaded.get(typeCode);
    return items ? { items, source: "api" } : { items: lookupFallbacks[typeCode], source: "fallback" };
  });
  const [generation, setGeneration] = useState(0);

  useEffect(() => {
    const listener = () => setGeneration((value) => value + 1);
    listeners.add(listener);
    return () => { listeners.delete(listener); };
  }, []);

  useEffect(() => {
    let current = true;
    void load(typeCode, token).then((items) => {
      if (current) setState(items && items.length > 0 ? { items, source: "api" } : { items: lookupFallbacks[typeCode], source: "fallback" });
    });
    return () => { current = false; };
  }, [typeCode, token, generation]);
  return state;
}
