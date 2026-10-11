import { useEffect, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { formatDateTime } from "../displaySettings.ts";
import { changeModule, datesValid, DeletedRecord, DeletedType, emptyRecycleFilters, formOptions, moduleOptions,
  recycleQuery, RecycleFilters, restoredMessage, shortId, withoutRecord } from "../recycleBinUi.ts";
import { Pager, SectionTitle } from "./shared";
import { ToastStack, useToast } from "./Toast";

type RecyclePage = Page<DeletedRecord> & { types: DeletedType[] };

// Administration → "Obrisani redovi": soft-deleted records with "Vrati" (restore).
export default function DeletedRecordsPage({ token }: { token: string }) {
  const [filters, setFilters] = useState<RecycleFilters>(emptyRecycleFilters);
  const [applied, setApplied] = useState<RecycleFilters>(emptyRecycleFilters);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<RecyclePage>({ page: 1, pageSize: 20, totalCount: 0, items: [], types: [] });
  const [types, setTypes] = useState<DeletedType[]>([]);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const { toasts, show, dismiss } = useToast();

  useEffect(() => {
    let current = true;
    setLoading(true);
    callApi<RecyclePage>(recycleQuery(applied, page), {}, token)
      .then((result) => { if (current) { setData(result); setTypes(result.types); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [token, applied, page, revision]);

  function refresh() {
    if (!datesValid(filters)) { setError("Datum „od“ mora biti prije datuma „do“."); return; }
    setPage(1); setApplied(filters); setRevision((value) => value + 1);
  }

  async function restore(record: DeletedRecord) {
    const key = `${record.entityType}:${record.recordId}`;
    setBusy(key); setError("");
    try {
      await callApi("/api/admin/recycle-bin/restore", { method: "POST",
        body: JSON.stringify({ entityType: record.entityType, recordId: record.recordId }) }, token);
      setData((current) => withoutRecord(current, record));
      show(restoredMessage(record));
    } catch (problem) { show(errorText(problem), "error"); }
    finally { setBusy(""); }
  }

  return <>
    <SectionTitle title="Obrisani redovi" description="Obrisani zapisi registra i dobavljača. „Vrati“ ih ponovo uključuje u sistem." />
    <div className="panel recycle-filters">
      <label>Modul<select value={filters.module} onChange={(event) => setFilters(changeModule(filters, event.target.value, types))}>
        <option value="">Sve forme</option>
        {moduleOptions(types).map((module) => <option key={module} value={module}>{module}</option>)}</select></label>
      <label>Forma<select value={filters.entityType} onChange={(event) => setFilters({ ...filters, entityType: event.target.value })}>
        <option value="">Sve</option>
        {formOptions(types, filters.module).map((type) => <option key={type.entityType} value={type.entityType}>{type.form}</option>)}</select></label>
      <label>Obrisano od<input type="date" value={filters.from} onChange={(event) => setFilters({ ...filters, from: event.target.value })} /></label>
      <label>do<input type="date" value={filters.to} onChange={(event) => setFilters({ ...filters, to: event.target.value })} /></label>
      <button type="button" onClick={refresh}>Osvježi</button>
    </div>
    {error && <p className="error" role="alert">{error}</p>}
    <section className="panel">
      <div className="table-scroll"><table className="recycle-table"><thead><tr><th>ID</th><th>Modul</th><th>Forma</th><th>ID zapisa</th>
        <th>Vrijeme brisanja</th><th>Obrisao</th><th>Akcija</th></tr></thead>
        <tbody>{data.items.map((record) => {
          const key = `${record.entityType}:${record.recordId}`;
          return <tr key={key}>
            <td>{record.id ?? "—"}</td><td>{record.module}</td><td>{record.form}</td>
            <td><span className="record-label">{record.label}</span><br /><code title={record.recordId}>{shortId(record.recordId)}</code></td>
            <td>{record.deletedAt ? formatDateTime(record.deletedAt) : "—"}</td><td>{record.deletedBy ?? "—"}</td>
            <td className="actions"><button type="button" disabled={busy !== ""} aria-label={`Vrati ${record.form}: ${record.label}`}
              onClick={() => void restore(record)}>{busy === key ? "Vraćanje…" : "Vrati"}</button></td>
          </tr>;
        })}</tbody></table></div>
      {!loading && data.items.length === 0 && <p className="empty-inline">Nema obrisanih zapisa za odabrane filtere.</p>}
      {loading && data.items.length === 0 && <p className="empty-inline">Učitavanje…</p>}
      <Pager {...data} onPage={setPage} />
    </section>
    <ToastStack toasts={toasts} onDismiss={dismiss} />
  </>;
}
