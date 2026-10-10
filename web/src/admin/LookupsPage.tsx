import { FormEvent, useEffect, useRef, useState } from "react";
import { callApi, errorText } from "../api";
import { draftOf, emptyDraft, filterTypes, itemCountLabel, LookupDraft, LookupItem, LookupType, requestBody,
  statusLabel, toggledBody, validateDraft } from "../lookupsUi.ts";
import { SectionTitle } from "./shared";

// Administration → "Šifarnici": code lists on the left, items of the selected list on the right.
export default function LookupsPage({ token }: { token: string }) {
  const [types, setTypes] = useState<LookupType[]>([]);
  const [selected, setSelected] = useState("");
  const [query, setQuery] = useState("");
  const [draft, setDraft] = useState<LookupDraft | null>(null);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [formError, setFormError] = useState("");
  const [notice, setNotice] = useState("");
  const firstField = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let current = true;
    callApi<LookupType[]>("/api/admin/lookups", {}, token)
      .then((result) => {
        if (!current) return;
        setTypes(result); setError("");
        setSelected((code) => result.some((type) => type.code === code) ? code : result[0]?.code ?? "");
      })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, revision]);

  useEffect(() => { if (draft) firstField.current?.focus(); }, [draft?.id, draft !== null]);

  const visible = filterTypes(types, query);
  const type = types.find((item) => item.code === selected);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || !type) return;
    const problem = validateDraft(draft, type);
    if (problem) { setFormError(problem); return; }
    setBusy(true); setFormError("");
    try {
      await callApi(draft.id === null ? `/api/admin/lookups/${type.code}/items` : `/api/admin/lookups/items/${draft.id}`,
        { method: draft.id === null ? "POST" : "PUT", body: JSON.stringify(requestBody(draft)) }, token);
      setNotice(draft.id === null ? "Stavka je dodana." : "Stavka je izmijenjena.");
      setDraft(null); setRevision((value) => value + 1);
    } catch (problem) { setFormError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function toggle(item: LookupItem) {
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/admin/lookups/items/${item.id}`, { method: "PUT", body: JSON.stringify(toggledBody(item)) }, token);
      setNotice(item.isActive ? `„${item.name}“ je deaktivirana (ostaje u postojećim zapisima).` : `„${item.name}“ je ponovo aktivna.`);
      setRevision((value) => value + 1);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <>
    <SectionTitle title="Šifarnici" description="Klasifikacije i opcije padajućih lista. Stavke se ne brišu nego deaktiviraju." />
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    <div className="lookups-layout">
      <section className="panel lookups-types" aria-label="Šifarnici">
        <label className="lookups-search">Pretraga<input type="search" value={query} placeholder="Naziv ili šifra"
          onChange={(event) => setQuery(event.target.value)} /></label>
        <ul>{visible.map((item) => <li key={item.code}><button type="button" aria-current={item.code === selected ? "true" : undefined}
          className={item.code === selected ? "active" : ""} onClick={() => { setSelected(item.code); setNotice(""); }}>
          <span><strong>{item.name}</strong><small>{item.code}</small></span>
          <span className="lookups-count" aria-label={`Broj stavki: ${itemCountLabel(item)}`}>{itemCountLabel(item)}</span>
        </button></li>)}</ul>
        {visible.length === 0 && <p className="empty-inline">Nema šifarnika za „{query}“.</p>}
      </section>
      <section className="panel lookups-items">
        {type ? <>
          <div className="panel-header"><div><h2>{type.name}</h2><p className="lookups-description">{type.description}
            {type.isSystem ? " · sistemski šifarnik" : ""}</p></div>
            <button type="button" className="primary" onClick={() => { setDraft(emptyDraft(type)); setFormError(""); }}>Dodaj stavku</button></div>
          <div className="table-scroll"><table><thead><tr><th>Šifra</th><th>Naziv</th><th>Redoslijed</th><th>Status</th><th>Akcije</th></tr></thead>
            <tbody>{type.items.map((item) => <tr key={item.id} className={item.isActive ? "" : "inactive"}>
              <td><code>{item.code}</code>{item.zevId && <small> · samo jedan ZEV</small>}</td><td>{item.name}</td>
              <td>{item.sortOrder}</td><td>{statusLabel(item)}</td>
              <td className="actions"><button type="button" onClick={() => { setDraft(draftOf(item)); setFormError(""); }}
                aria-label={`Uredi ${item.name}`}>Uredi</button>
                <button type="button" disabled={busy} onClick={() => void toggle(item)}
                  aria-label={`${item.isActive ? "Deaktiviraj" : "Aktiviraj"} ${item.name}`}>{item.isActive ? "Deaktiviraj" : "Aktiviraj"}</button></td>
            </tr>)}</tbody></table></div>
          {type.items.length === 0 && <p className="empty-inline">Šifarnik nema stavki.</p>}
        </> : <p className="empty-inline">Odaberite šifarnik.</p>}
      </section>
    </div>
    {draft && type && <div className="modal-backdrop" onClick={(event) => { if (event.target === event.currentTarget) setDraft(null); }}>
      <div className="modal panel" role="dialog" aria-modal="true" aria-labelledby="lookup-dialog-title"
        onKeyDown={(event) => { if (event.key === "Escape") setDraft(null); }}>
        <h2 id="lookup-dialog-title">{draft.id === null ? `Nova stavka · ${type.name}` : `Uređivanje · ${draft.name || draft.code}`}</h2>
        <form className="admin-form" onSubmit={save}>
          <label>Šifra<input ref={draft.id === null ? firstField : undefined} value={draft.code} disabled={draft.id !== null}
            maxLength={50} onChange={(event) => setDraft({ ...draft, code: event.target.value })} /></label>
          {draft.id !== null && <p className="lookups-hint">Šifra se ne mijenja jer je čuvaju postojeći zapisi.</p>}
          <label>Naziv<input ref={draft.id !== null ? firstField : undefined} value={draft.name} maxLength={200}
            onChange={(event) => setDraft({ ...draft, name: event.target.value })} /></label>
          <label>Redoslijed<input type="number" min={0} max={100000} step={1} value={draft.sortOrder}
            onChange={(event) => setDraft({ ...draft, sortOrder: event.target.value })} /></label>
          {draft.id !== null && <label className="checkbox"><input type="checkbox" checked={draft.isActive}
            onChange={(event) => setDraft({ ...draft, isActive: event.target.checked, isDefault: event.target.checked && draft.isDefault })} /> Aktivna</label>}
          <label className="checkbox"><input type="checkbox" checked={draft.isDefault} disabled={!draft.isActive}
            onChange={(event) => setDraft({ ...draft, isDefault: event.target.checked })} /> Zadana vrijednost u formama</label>
          {formError && <p className="error" role="alert">{formError}</p>}
          <div className="form-actions"><button type="submit" disabled={busy}>{busy ? "Snimanje…" : "Snimi"}</button>
            <button type="button" className="secondary" onClick={() => setDraft(null)}>Odustani</button></div>
        </form>
      </div>
    </div>}
  </>;
}
