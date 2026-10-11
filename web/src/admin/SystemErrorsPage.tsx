import { useEffect, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { formatDateTime } from "../displaySettings.ts";
import { errorDraftChanged, ErrorFilter, errorFilters, errorStatusLabel, sourceBadge, SystemError } from "../systemOpsUi.ts";
import { Pager, SectionTitle } from "./shared";

// Administration → "Greške u sistemu": errors list on the left, the selected error on the right.
export default function SystemErrorsPage({ token }: { token: string }) {
  const [filter, setFilter] = useState<ErrorFilter>("active");
  const [page, setPage] = useState(1);
  const [errors, setErrors] = useState<Page<SystemError>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [resolved, setResolved] = useState(false);
  const [notes, setNotes] = useState("");
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let current = true;
    callApi<Page<SystemError>>(`/api/system/errors?status=${filter}&page=${page}`, {}, token)
      .then((result) => { if (current) { setErrors(result); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, filter, page, revision]);

  const selected = errors.items.find((item) => item.id === selectedId) ?? null;
  function select(item: SystemError) {
    setSelectedId(item.id); setResolved(item.status === "Resolved"); setNotes(item.adminNotes ?? ""); setNotice("");
  }

  async function save() {
    if (!selected) return;
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/system/errors/${selected.id}`, { method: "PUT",
        body: JSON.stringify({ resolved, adminNotes: notes }) }, token);
      setNotice(resolved ? `Greška #${selected.id} je označena kao završena.` : `Greška #${selected.id} je snimljena.`);
      setRevision((value) => value + 1);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <>
    <SectionTitle title="Greške u sistemu" description="Neobrađene greške API-ja i greške prijavljene iz web aplikacije." />
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    <div className="errors-layout">
      <section className="panel" aria-label="Lista grešaka">
        <div className="filter-tabs" role="tablist" aria-label="Status">{errorFilters.map((item) => <button type="button" key={item.value}
          role="tab" aria-selected={filter === item.value} className={filter === item.value ? "active" : ""}
          onClick={() => { setFilter(item.value); setPage(1); setSelectedId(null); }}>{item.label}</button>)}</div>
        <div className="table-scroll"><table><thead><tr><th>ID</th><th>Naziv</th><th>Izvor</th><th>Greška</th><th>Vrijeme</th></tr></thead>
          <tbody>{errors.items.map((item) => {
            const badge = sourceBadge(item.source);
            return <tr key={item.id} className={item.id === selectedId ? "selected" : ""} onClick={() => select(item)}>
              <td><button type="button" className="link-button" onClick={() => select(item)} aria-label={`Otvori grešku ${item.id}`}>#{item.id}</button></td>
              <td className="error-title">{item.title}</td><td><span className={badge.className}>{badge.label}</span></td>
              <td><code>{item.errorType}</code></td><td>{formatDateTime(item.createdAt)}</td></tr>;
          })}</tbody></table></div>
        {errors.items.length === 0 && <p className="empty-inline">Nema grešaka za odabrani status.</p>}
        <Pager {...errors} onPage={setPage} />
      </section>
      <section className="panel error-detail" aria-label="Detalj greške">
        {selected ? <>
          <h2>#{selected.id} · {selected.title}</h2>
          <p><span className={sourceBadge(selected.source).className}>{selected.source}</span> · {errorStatusLabel(selected.status)}{" "}
            · {formatDateTime(selected.createdAt)}{selected.resolvedAt ? ` · završeno ${formatDateTime(selected.resolvedAt)}` : ""}</p>
          <h3>Opis</h3><p className="error-message">{selected.message}</p>
          {selected.requestPath && <p><strong>Lokacija:</strong> <code>{selected.requestPath}</code></p>}
          {selected.stackTrace && <details><summary>Tehnički detalji</summary><pre className="stack">{selected.stackTrace}</pre></details>}
          <div className="admin-form">
            <label className="checkbox"><input type="checkbox" checked={resolved} onChange={(event) => setResolved(event.target.checked)} /> Završen</label>
            <label>Bilješka administratora<textarea rows={4} maxLength={2000} value={notes} onChange={(event) => setNotes(event.target.value)} /></label>
            <button type="button" disabled={busy || !errorDraftChanged(selected, resolved, notes)} onClick={() => void save()}>
              {busy ? "Snimanje…" : "Snimi"}</button>
          </div>
        </> : <p className="empty-inline">Odaberite grešku iz liste.</p>}
      </section>
    </div>
  </>;
}
