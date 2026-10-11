import { ChangeEvent, useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { loadBlobUrl } from "../authBlob.ts";
import { formatDate, formatDateTime, formatNumber } from "../displaySettings.ts";
import { label, maintenanceStatusLabels } from "../uiLabels.ts";
import { attachmentAccept, attachmentLabel, AttachmentType, attachmentTypes, completionBody, CompletionDraft, fileSizeLabel,
  completionProblem, isImage, validateAttachment } from "../workOrderDossier.ts";
import { readFileBase64 } from "../zevImage.ts";

type Attachment = { id: string; attachmentType: string; fileName: string; contentType: string; length: number; uploadedAt: string };
type Dossier = {
  id: string; title: string; description: string; status: string; dueOn: string | null; supplierId: string | null;
  supplierName: string | null; contractorName: string | null; contractorPhone: string | null; invoiceNumber: string | null;
  costAmount: number | null; expenseFund: string | null; expenseId: string | null; completedDate: string | null;
  workDescription: string | null; readOnly: boolean; rowVersion: string;
  schedule: { id: string; dueOn: string; planTitle: string; category: string; status: string } | null;
  ticket: { id: string; category: string; location: string; description: string; status: string } | null;
  attachments: Attachment[]; history: { status: string; changedUtc: string; actor: string | null }[];
};
type Supplier = { id: string; name: string };
const fundLabels: Record<string, string> = { Regular: "Redovni fond", Reserve: "Rezervni fond" };

function AttachmentPreview({ path, token, item }: { path: string; token: string; item: Attachment }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => { if (isImage(item.contentType)) void loadBlobUrl(path, token).then(setUrl); }, [path, token, item.contentType]);
  async function open() { const blob = url ?? await loadBlobUrl(path, token); if (blob) window.open(blob, "_blank", "noopener"); }
  return <figure className={`dossier-file ${isImage(item.contentType) ? "image" : "document"}`}>
    {isImage(item.contentType) ? (url ? <img src={url} alt={item.fileName} /> : <span className="dossier-thumb-loading">…</span>)
      : <span className="dossier-pdf" aria-hidden="true">PDF</span>}
    <figcaption><button type="button" className="link-button" onClick={() => void open()}>{item.fileName}</button>
      <small>{attachmentLabel(item.attachmentType)} · {fileSizeLabel(item.length)}</small></figcaption>
  </figure>;
}

// Digital file of a work order: contractor, finance, attachments, history; read-only once the order is completed.
export default function WorkOrderDossier({ token, zevId, orderId, canEdit, suppliers, onClose }: {
  token: string; zevId: string; orderId: string; canEdit: boolean; suppliers: Supplier[]; onClose: () => void;
}) {
  const base = `/api/zevs/${zevId}/maintenance/work-orders/${orderId}`;
  const [dossier, setDossier] = useState<Dossier | null>(null);
  const [revision, setRevision] = useState(0);
  const [kind, setKind] = useState<AttachmentType>("Invoice");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [draft, setDraft] = useState<CompletionDraft>({ completedDate: new Date().toISOString().slice(0, 10), workDescription: "",
    invoiceNumber: "", costAmount: "", expenseFund: "Regular", supplierId: "", contractorName: "", contractorPhone: "", recordExpense: false });

  useEffect(() => {
    let current = true;
    callApi<Dossier>(base, {}, token).then((result) => {
      if (!current) return;
      setDossier(result);
      setDraft((value) => ({ ...value, supplierId: value.supplierId || result.supplierId || "",
        contractorName: value.contractorName || result.contractorName || "", contractorPhone: value.contractorPhone || result.contractorPhone || "",
        costAmount: value.costAmount || (result.costAmount?.toString() ?? ""), invoiceNumber: value.invoiceNumber || (result.invoiceNumber ?? "") }));
    }).catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [base, token, revision]);

  async function upload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    const problem = validateAttachment(file);
    if (problem) { setError(problem); return; }
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`${base}/attachments`, { method: "POST", body: JSON.stringify({ fileName: file.name, contentType: file.type,
        contentBase64: await readFileBase64(file), attachmentType: kind }) }, token);
      setNotice(`Dodano: ${file.name}`); setRevision((value) => value + 1);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function remove(item: Attachment) {
    if (!window.confirm(`Ukloniti prilog „${item.fileName}“?`)) return;
    setBusy(true); setError("");
    try { await callApi(`${base}/attachments/${item.id}`, { method: "DELETE" }, token); setRevision((value) => value + 1); }
    catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function complete() {
    if (!dossier) return;
    const problem = completionProblem(draft);
    if (problem) { setError(problem); return; }
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`${base}/complete`, { method: "PUT", body: JSON.stringify(completionBody(draft, dossier.rowVersion)) }, token);
      setNotice("Radni nalog je završen; povezani termin i prijava su zatvoreni."); setRevision((value) => value + 1);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  const set = (patch: Partial<CompletionDraft>) => setDraft((value) => ({ ...value, ...patch }));
  const editable = Boolean(dossier && !dossier.readOnly && canEdit);
  const groups: { title: string; types: string[] }[] = [
    { title: "Računi i izvještaji", types: ["Invoice", "WorkReport"] }, { title: "Slike prije", types: ["PhotoBefore"] },
    { title: "Slike poslije", types: ["PhotoAfter"] }];
  return <div className="modal-backdrop" onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="modal panel dossier" role="dialog" aria-modal="true" aria-labelledby="dossier-title"
      onKeyDown={(event) => { if (event.key === "Escape") onClose(); }}>
      {!dossier ? <p>{error || "Učitavanje…"}</p> : <>
        <div className="dossier-head"><div><p className="eyebrow">RADNI NALOG</p><h2 id="dossier-title">{dossier.title}</h2>
          <p>{dossier.description}</p></div>
          <span className={`badge dossier-status ${dossier.status.toLowerCase()}`}>{label(maintenanceStatusLabels, dossier.status)}</span>
          <button type="button" className="secondary" onClick={onClose} aria-label="Zatvori dosije">✕</button></div>
        {dossier.readOnly && <p className="notice">Završen nalog je arhiviran dosije — samo za čitanje.</p>}
        {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
        <div className="dossier-grid">
          <section><h3>Povezano</h3>
            {dossier.schedule && <p>Plan: <strong>{dossier.schedule.planTitle}</strong> ({dossier.schedule.category}), termin {formatDate(dossier.schedule.dueOn)}
              · {label(maintenanceStatusLabels, dossier.schedule.status)}</p>}
            {dossier.ticket && <p>Prijava: <strong>{dossier.ticket.category}</strong> · {dossier.ticket.location} · {label(maintenanceStatusLabels, dossier.ticket.status)}</p>}
            {!dossier.schedule && !dossier.ticket && <p className="empty-inline">Samostalan nalog.</p>}
            <p>Rok: {dossier.dueOn ? formatDate(dossier.dueOn) : "—"}</p></section>
          <section><h3>Izvođač</h3>
            {editable ? <div className="admin-form compact">
              <label>Dobavljač iz šifarnika<select value={draft.supplierId} onChange={(event) => set({ supplierId: event.target.value })}>
                <option value="">— vanjski izvođač —</option>{suppliers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
              {!draft.supplierId && <><label>Naziv izvođača<input maxLength={200} value={draft.contractorName} onChange={(event) => set({ contractorName: event.target.value })} /></label>
                <label>Telefon<input maxLength={40} value={draft.contractorPhone} onChange={(event) => set({ contractorPhone: event.target.value })} /></label></>}
            </div> : <p>{dossier.supplierName ?? dossier.contractorName ?? "—"}{dossier.contractorPhone ? ` · ${dossier.contractorPhone}` : ""}</p>}</section>
          <section><h3>Finansije i realizacija</h3>
            {editable ? <div className="admin-form compact">
              <label>Datum završetka<input type="date" value={draft.completedDate} onChange={(event) => set({ completedDate: event.target.value })} /></label>
              <label>Opis izvršenog posla<textarea rows={3} maxLength={4000} value={draft.workDescription} onChange={(event) => set({ workDescription: event.target.value })} /></label>
              <label>Broj računa<input maxLength={100} value={draft.invoiceNumber} onChange={(event) => set({ invoiceNumber: event.target.value })} /></label>
              <label>Iznos (KM)<input type="number" min="0" step="0.01" value={draft.costAmount} onChange={(event) => set({ costAmount: event.target.value })} /></label>
              <label>Fond<select value={draft.expenseFund} onChange={(event) => set({ expenseFund: event.target.value as CompletionDraft["expenseFund"] })}>
                <option value="Regular">Redovni fond</option><option value="Reserve">Rezervni fond</option></select></label>
              <label className="checkbox"><input type="checkbox" checked={draft.recordExpense} onChange={(event) => set({ recordExpense: event.target.checked })} />
                Upiši trošak u rashode (nacrt, knjiži se u Rashodima)</label>
              <button type="button" disabled={busy} onClick={() => void complete()}>{busy ? "Snimanje…" : "Završi nalog"}</button>
            </div> : <dl className="dossier-facts">
              <dt>Završeno</dt><dd>{dossier.completedDate ? formatDate(dossier.completedDate) : "—"}</dd>
              <dt>Izvršeni posao</dt><dd>{dossier.workDescription ?? "—"}</dd>
              <dt>Račun</dt><dd>{dossier.invoiceNumber ?? "—"}</dd>
              <dt>Iznos</dt><dd>{dossier.costAmount === null ? "—" : `${formatNumber(dossier.costAmount)} KM`}</dd>
              <dt>Fond</dt><dd>{dossier.expenseFund ? fundLabels[dossier.expenseFund] : "—"}</dd>
              <dt>Rashod</dt><dd>{dossier.expenseId ? "Upisan u rashode" : "Nije upisan"}</dd></dl>}</section>
        </div>
        <section className="dossier-files"><h3>Prilozi</h3>
          {editable && <div className="dossier-upload">
            <label>Vrsta<select value={kind} onChange={(event) => setKind(event.target.value as AttachmentType)}>
              {attachmentTypes.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
            <label className="file-button">Dodaj prilog<input type="file" accept={attachmentAccept} disabled={busy} onChange={(event) => void upload(event)}
              aria-label="Dodaj prilog radnom nalogu" /></label>
            <small>PDF, JPG ili PNG, najviše 10 MB.</small></div>}
          {groups.map((group) => {
            const items = dossier.attachments.filter((item) => group.types.includes(item.attachmentType));
            return <div key={group.title} className="dossier-group"><h4>{group.title}</h4>
              {items.length === 0 ? <p className="empty-inline">Nema priloga.</p> : <div className="dossier-gallery">{items.map((item) =>
                <div key={item.id} className="dossier-item"><AttachmentPreview path={`${base}/attachments/${item.id}`} token={token} item={item} />
                  {editable && <button type="button" className="danger" disabled={busy} onClick={() => void remove(item)}>Ukloni</button>}</div>)}</div>}</div>;
          })}</section>
        <section><h3>Revizija</h3><ul className="dossier-history">{dossier.history.map((item, index) =>
          <li key={index}>{formatDateTime(item.changedUtc)} · {label(maintenanceStatusLabels, item.status)} · {item.actor ?? "sistem"}</li>)}</ul></section>
      </>}
    </div></div>;
}
