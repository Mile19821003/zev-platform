import { useEffect, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { resourceKey } from "../dataSync.ts";
import { useChangeRevision } from "../useChangeRevision.ts";

type Option = { id: string; name?: string; displayName?: string; email?: string; code?: string };

export function Lookup({ token, label, path, value, onChange, disabled = false, required = false }: {
  token: string;
  label: string;
  path: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  required?: boolean;
}) {
  const [search, setSearch] = useState("");
  const [options, setOptions] = useState<Option[]>([]);
  const [selected, setSelected] = useState<Option | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const revision = useChangeRevision(resourceKey(path) ?? "");

  useEffect(() => { setSelected(null); setSearch(""); }, [path]);

  useEffect(() => {
    if (!path) { setOptions([]); return; }
    const controller = new AbortController();
    setLoading(true);
    const separator = path.includes("?") ? "&" : "?";
    callApi<Page<Option>>(`${path}${separator}pageSize=100&search=${encodeURIComponent(search)}`, { signal: controller.signal }, token)
      .then((result) => { if (!controller.signal.aborted) { setOptions(result.items); setError(""); } })
      .catch((problem) => { if (!controller.signal.aborted) setError(errorText(problem)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [path, search, token, revision]);

  const available = selected && !options.some((item) => item.id === selected.id) ? [selected, ...options] : options;
  return (
    <div className="lookup-field">
      <label>{label}
        <input aria-label={`Pretraži ${label.toLowerCase()}`} value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pretraga…" disabled={disabled} />
        <select value={value} required={required} onChange={(event) => {
          const option = available.find((item) => item.id === event.target.value) ?? null;
          setSelected(option);
          onChange(event.target.value);
        }} disabled={disabled || loading}>
          <option value="">Odaberite</option>
          {available.map((item) => <option key={item.id} value={item.id}>
            {item.name ?? item.displayName ?? item.email ?? item.code ?? item.id} {item.name && item.code ? `(${item.code})` : ""}
          </option>)}
        </select>
      </label>
      {loading && <small>Učitavanje…</small>}
      {error && <small className="field-error">{error}</small>}
    </div>
  );
}

export function Pager({ page, pageSize, totalCount, onPage }: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
}) {
  return <div className="pager">
    <span>{totalCount} zapisa · stranica {page}</span>
    <div>
      <button type="button" onClick={() => onPage(page - 1)} disabled={page <= 1}>Prethodna</button>
      <button type="button" onClick={() => onPage(page + 1)} disabled={page * pageSize >= totalCount}>Sljedeća</button>
    </div>
  </div>;
}

export function Status({ active }: { active: boolean }) {
  return <span className={`status ${active ? "active" : "inactive"}`}>{active ? "Aktivan" : "Neaktivan"}</span>;
}

// eyebrow={null} hides the caption, e.g. on owner-facing pages that are not administration.
export function SectionTitle({ title, description, eyebrow = "ADMINISTRACIJA" }: {
  title: string; description: string; eyebrow?: string | null;
}) {
  return <div className="admin-heading">{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h1>{title}</h1><p>{description}</p></div>;
}
