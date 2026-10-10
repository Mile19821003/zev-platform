import { useEffect, useRef, useState } from "react";
import { callApi, errorText } from "../api";
import { paletteIcon, searchIcons } from "../iconRegistry.ts";
import { iconCatalog, iconFor } from "../navigationModel.ts";
import { useChangeRevision } from "../useChangeRevision.ts";
import { NavIcon } from "./WorkspaceNav";
import { SectionTitle } from "./shared";

type IconsResponse = { icons: Record<string, string>; palette: string[] };

// Grid of all palette icons with a quick search; Enter/click picks, Escape closes.
export function IconPicker({ title, selected, onPick, onClose }: { title: string; selected: string;
  onPick: (key: string) => void; onClose: () => void }) {
  const [query, setQuery] = useState("");
  const search = useRef<HTMLInputElement>(null);
  useEffect(() => { search.current?.focus(); }, []);
  const icons = searchIcons(query);
  return <div className="modal-backdrop" onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="modal panel icon-picker" role="dialog" aria-modal="true" aria-labelledby="icon-picker-title"
      onKeyDown={(event) => { if (event.key === "Escape") onClose(); }}>
      <h2 id="icon-picker-title">{title}</h2>
      <label className="lookups-search">Pretraga ikonica<input ref={search} type="search" value={query}
        placeholder="npr. novac, šifarnik, zgrada" onChange={(event) => setQuery(event.target.value)} /></label>
      <div className="icon-grid" role="listbox" aria-label="Paleta ikonica">{icons.map((icon) =>
        <button type="button" key={icon.key} role="option" aria-selected={icon.key === selected}
          className={icon.key === selected ? "selected" : ""} title={`${icon.label} (${icon.key})`}
          onClick={() => onPick(icon.key)}><NavIcon name={icon.key} size={26} /><span>{icon.label}</span></button>)}</div>
      {icons.length === 0 && <p className="empty-inline">Nema ikonica za „{query}“.</p>}
      <div className="form-actions"><button type="button" className="secondary" onClick={onClose}>Zatvori</button></div>
    </div>
  </div>;
}

// Administration → "Prikazi i ikone": icon per category and module, used by the dashboard and the sidebar.
export default function IconSettingsPage({ token }: { token: string }) {
  const [saved, setSaved] = useState<Record<string, string> | null>(null);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [picking, setPicking] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const revision = useChangeRevision("module-icons");

  useEffect(() => {
    let current = true;
    callApi<IconsResponse>("/api/system/module-icons", {}, token)
      .then((result) => { if (current) { setSaved(result.icons); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, revision]);

  const catalog = iconCatalog();
  const current = (moduleKey: string) => draft[moduleKey] ?? iconFor(moduleKey, saved ?? {});
  const changes = Object.entries(draft).filter(([key, icon]) => icon !== iconFor(key, saved ?? {}))
    .map(([moduleKey, iconKey]) => ({ moduleKey, iconKey }));

  async function save() {
    if (changes.length === 0) return;
    setBusy(true); setError(""); setNotice("");
    try {
      // The successful PUT publishes the "module-icons" change; navigation and dashboard update without F5.
      const result = await callApi<IconsResponse>("/api/system/module-icons", { method: "PUT",
        body: JSON.stringify({ changes }) }, token);
      setSaved(result.icons); setDraft({});
      setNotice(`Snimljeno izmjena: ${changes.length}. Navigacija je ažurirana.`);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  if (!saved) return <>{error ? <p className="error" role="alert">{error}</p> : <p>Učitavanje…</p>}</>;
  const entry = catalog.find((item) => item.moduleKey === picking);
  return <>
    <SectionTitle title="Prikazi i ikone" description="Ikonica za svaku kategoriju i modul na nadzornoj ploči i u lijevom meniju." />
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    <section className="panel">
      <div className="panel-header"><h2>Moduli i kategorije</h2><div className="form-actions">
        {changes.length > 0 && <span className="icon-changes">Nesačuvanih izmjena: {changes.length}</span>}
        <button type="button" className="secondary" disabled={busy || Object.keys(draft).length === 0} onClick={() => setDraft({})}>Odbaci</button>
        <button type="button" disabled={busy || changes.length === 0} onClick={() => void save()}>{busy ? "Snimanje…" : "Snimi izmjene"}</button>
      </div></div>
      <div className="table-scroll"><table><thead><tr><th>Kategorija</th><th>Modul</th><th>Trenutna ikonica</th><th>Akcija</th></tr></thead>
        <tbody>{catalog.map((item) => {
          const icon = paletteIcon(current(item.moduleKey));
          const changed = draft[item.moduleKey] !== undefined && draft[item.moduleKey] !== iconFor(item.moduleKey, saved);
          return <tr key={item.moduleKey} className={changed ? "changed" : ""}><td>{item.category}</td><td>{item.label}</td>
            <td><span className="icon-current"><NavIcon name={icon.key} size={22} /><span>{icon.label}</span><code>{icon.key}</code></span></td>
            <td className="actions"><button type="button" aria-label={`Promijeni ikonicu: ${item.category} · ${item.label}`} onClick={() => setPicking(item.moduleKey)}>Promijeni</button></td></tr>;
        })}</tbody></table></div>
    </section>
    {entry && <IconPicker title={`Ikonica · ${entry.category} · ${entry.label}`} selected={current(entry.moduleKey)}
      onPick={(key) => { setDraft((value) => ({ ...value, [entry.moduleKey]: key })); setPicking(null); setNotice(""); }}
      onClose={() => setPicking(null)} />}
  </>;
}
