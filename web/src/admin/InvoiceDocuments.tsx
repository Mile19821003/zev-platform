import { useEffect, useRef, useState } from "react";
import { callApi, errorText } from "../api";
import { canCorrect, canStorno, coalesceTimelineEvents, correctionSignedTotal, documentStatusLabel,
  timelineEventLabel, paymentSplitLabel, validateCorrectionLines, validateDocumentReason } from "../invoiceLifecycleUi.ts";
import type { CorrectionLineDraft, CorrectionType } from "../invoiceLifecycleUi.ts";
import { useChangeRevision } from "../useChangeRevision.ts";
import { formatMoney } from "../currency.ts";
import { formatDateTime } from "../displaySettings.ts";

type Document = { id: string; documentNumber: string; type: string; issueDate: string;
  reason: string; note: string | null; signedAmount: number; createdByUserId: string | null;
  actorName: string | null; createdUtc: string };
type TimelineEvent = { occurredUtc: string; type: string; number: string | null;
  amount: number; sourceId: string; date: string; actorName: string | null;
  financialDocumentId: string | null; allocated?: number | null; credit?: number | null;
  appliedToInvoice?: number | null; currencyCode?: string | null; reason?: string | null;
  allocationDetails?: { chargeId: string; invoiceNumber: string | null; unitCode: string;
    description: string | null; amount: number }[] | null };
type SourceLine = { id: string | null; fundId: string; name: string; amount: number };
type Fund = { id: string; name: string };
type ExtraLine = { key: string; fundId: string; description: string; amount: string };

export default function InvoiceDocuments({ zevId, invoiceId, status, token, canReverse = false,
  canAmend = false, lines = [], funds = [], onChanged, currencyCode = "UNKNOWN" }: { zevId: string; invoiceId: string | null;
  status: string; token: string; canReverse?: boolean; canAmend?: boolean;
  lines?: SourceLine[]; funds?: Fund[]; onChanged?: () => void; currencyCode?: string }) {
  const [documents, setDocuments] = useState<Document[]>([]);
  const [timeline, setTimeline] = useState<TimelineEvent[]>([]);
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const [type, setType] = useState<CorrectionType>("Credit");
  const [correctionOpen, setCorrectionOpen] = useState(false);
  const [selectedAmounts, setSelectedAmounts] = useState<Record<string, string>>({});
  const [extraLines, setExtraLines] = useState<ExtraLine[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef("");
  const revision = useChangeRevision(`zev:${zevId}:finance`);
  const correctionLines: CorrectionLineDraft[] = [
    ...lines.filter((line) => line.id && Object.hasOwn(selectedAmounts, line.id)).map((line) => ({
      originalInvoiceLineId: line.id, fundId: null, description: null,
      amount: Number(selectedAmounts[line.id!]),
    })),
    ...extraLines.map((line) => ({ originalInvoiceLineId: null, fundId: line.fundId,
      description: line.description, amount: Number(line.amount) })),
  ];
  const signedTotal = correctionSignedTotal(type, correctionLines);
  useEffect(() => {
    setCorrectionOpen(false); setSelectedAmounts({}); setExtraLines([]);
    setReason(""); setNote(""); requestId.current = "";
  }, [zevId, invoiceId]);
  useEffect(() => {
    if (!invoiceId) { setDocuments([]); setTimeline([]); return; }
    let active = true;
    const path = `/api/zevs/${zevId}/finance/invoices/${invoiceId}`;
    void Promise.all([
      callApi<{ documents: Document[] }>(`${path}/documents`, {}, token),
      callApi<TimelineEvent[]>(`${path}/timeline`, {}, token),
    ]).then(([related, history]) => {
      if (active) { setDocuments(related.documents); setTimeline(history); setError(""); }
    }).catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [zevId, invoiceId, token, revision]);

  async function download(document: Document) {
    setError("");
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/finance/documents/${document.id}.pdf`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("PDF povezanog dokumenta nije dostupan.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = window.document.createElement("a");
      anchor.href = url; anchor.download = `${document.documentNumber}.pdf`; anchor.click();
      URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
  }

  async function submit(action: "storno" | "corrections") {
    if (!invoiceId || pending.current) return;
    const reasonError = validateDocumentReason(reason);
    const correctionError = action === "corrections" ? validateCorrectionLines(type, correctionLines) : null;
    if (reasonError || correctionError) { setError(reasonError ?? correctionError ?? ""); return; }
    if (action === "storno" && !window.confirm(
      "Storno finansijskog dokumenta se ne može poništiti direktnim uređivanjem. Originalni račun ostaje sačuvan. Potvrdi storno?")) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    requestId.current ||= crypto.randomUUID();
    const body = action === "storno" ? { requestId: requestId.current, reason: reason.trim(), note: note.trim() || null }
      : { requestId: requestId.current, type, reason: reason.trim(), note: note.trim() || null,
        lines: correctionLines };
    try {
      await callApi(`/api/zevs/${zevId}/finance/invoices/${invoiceId}/${action}`,
        { method: "POST", body: JSON.stringify(body) }, token);
      setNotice(action === "storno" ? "Storno je knjižen." : "Korekcija je knjižena.");
      setReason(""); setNote(""); setSelectedAmounts({}); setExtraLines([]);
      setCorrectionOpen(false); requestId.current = "";
      onChanged?.();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  if (!invoiceId) return <p>LEGACY OBRAČUN – formalni storno i korekcija nisu dostupni.</p>;
  return <section className="panel">
    <h3>Istorija dokumenta · {documentStatusLabel(status)}</h3>
    {error && <p className="error" role="alert">{error}</p>}
    {notice && <p className="notice" role="status">{notice}</p>}
    {documents.length === 0 ? <p className="empty-inline">Nema povezanih dokumenata.</p>
      : <ul>{documents.map((item) => <li key={item.id}>
        {item.issueDate} · Evidentirano: {formatDateTime(item.createdUtc)}
        {" "}· {timelineEventLabel(item.type)}
        {" "}· {item.documentNumber} · {formatMoney(item.signedAmount, currencyCode)} · {item.reason}
        {" "}· {item.type === "Storno" ? "Stornirao" : "Korigovao"}: {item.actorName ?? "Nepoznat korisnik"}
        <button type="button" onClick={() => void download(item)}>PDF dokument</button>
      </li>)}</ul>}
    {timeline.length > 0 && <><h4>Timeline</h4><ol>{coalesceTimelineEvents(timeline).map((item, index) => {
      const related = documents.find((document) => document.id === item.sourceId);
      return <li key={`${item.sourceId}-${index}`}>
        {item.date} · Evidentirano: {formatDateTime(item.occurredUtc)}
        {" "}· {timelineEventLabel(item.type)}
        {item.number ? ` · ${item.number}` : ""} · {item.type === "Payment" && item.appliedToInvoice != null
          ? paymentSplitLabel(item.amount, item.appliedToInvoice, item.currencyCode ?? currencyCode)
          : formatMoney(item.amount, item.currencyCode ?? currencyCode)}
        {item.reason ? ` · Razlog: ${item.reason}` : ""}
        {item.actorName ? ` · ${item.actorName}` : ""}
        {related && <button type="button" onClick={() => void download(related)}>Otvori dokument</button>}
        {item.type === "Payment" && item.allocationDetails && <details><summary>Detalji raspoređivanja</summary>
          <ul>{item.allocationDetails.map((allocation, allocationIndex) => <li key={`${allocation.chargeId}-${allocationIndex}`}>
            {allocation.invoiceNumber ?? "Legacy"} / {allocation.unitCode} / {allocation.description ?? "Zaduženje"}
            · {formatMoney(allocation.amount, item.currencyCode ?? currencyCode)}</li>)}</ul>
          <p>Raspoređeno: {formatMoney(item.allocated ?? 0, item.currencyCode ?? currencyCode)} · Kredit: {
            formatMoney(item.credit ?? 0, item.currencyCode ?? currencyCode)}</p>
        </details>}
      </li>;
    })}</ol></>}
    {status === "Corrected" && <p>Račun ima korektivne dokumente. Direktno storno nije dozvoljeno.</p>}
    {(canStorno(invoiceId, status, canReverse) || canCorrect(invoiceId, status, canAmend)) && <>
      {canStorno(invoiceId, status, canReverse) && <button type="button" disabled={busy}
        onClick={() => void submit("storno")}>STORNIRAJ RAČUN</button>}
      {canCorrect(invoiceId, status, canAmend) && <button type="button" onClick={() => setCorrectionOpen((open) => !open)}>
        {correctionOpen ? "Zatvori korekciju" : "KREIRAJ KOREKCIJU"}</button>}
      {(canStorno(invoiceId, status, canReverse) || correctionOpen && canCorrect(invoiceId, status, canAmend)) && <>
        <label>Razlog * <input value={reason} maxLength={500} onChange={(event) => {
          requestId.current = ""; setReason(event.target.value); }} /></label>
        <label>Napomena <input value={note} maxLength={1000} onChange={(event) => {
          requestId.current = ""; setNote(event.target.value); }} /></label>
      </>}
      {correctionOpen && canCorrect(invoiceId, status, canAmend) && <div>
        <label>Tip korekcije <select value={type} onChange={(event) => {
          requestId.current = ""; const selectedType = event.target.value as CorrectionType;
          setType(selectedType); if (selectedType === "Credit") setExtraLines([]); }}>
          <option value="Credit">UMANJENJE / KNJIŽNO ODOBRENJE</option>
          <option value="Debit">DODATNO ZADUŽENJE / KNJIŽNO TEREĆENJE</option>
        </select></label>
        <div className="table-wrap"><table><thead><tr><th>Koriguj</th><th>Izvorna stavka</th><th>Original</th><th>Korekcija ({currencyCode})</th></tr></thead>
          <tbody>{lines.filter((line) => line.id).map((line) => <tr key={line.id!}>
            <td><input type="checkbox" aria-label={`Koriguj ${line.name}`}
              checked={Object.hasOwn(selectedAmounts, line.id!)} onChange={(event) => {
                requestId.current = "";
                setSelectedAmounts((current) => { const next = { ...current };
                  if (event.target.checked) next[line.id!] = ""; else delete next[line.id!]; return next; });
              }} /></td><td>{line.name}</td><td>{formatMoney(line.amount, currencyCode)}</td>
            <td><input type="number" min="0.01" step="0.01" disabled={!Object.hasOwn(selectedAmounts, line.id!)}
              value={selectedAmounts[line.id!] ?? ""} onChange={(event) => {
                requestId.current = ""; setSelectedAmounts((current) => ({ ...current, [line.id!]: event.target.value }));
              }} /></td></tr>)}</tbody></table></div>
        {type === "Debit" && <><button type="button" onClick={() => {
          requestId.current = ""; setExtraLines((current) => [...current,
            { key: crypto.randomUUID(), fundId: "", description: "", amount: "" }]);
        }}>+ DODAJ KOREKTIVNU STAVKU</button>
          {extraLines.map((line) => <div className="finance-grid" key={line.key}>
            <label>Fond <select value={line.fundId} onChange={(event) => {
              requestId.current = ""; setExtraLines((current) => current.map((item) => item.key === line.key
                ? { ...item, fundId: event.target.value } : item)); }}>
              <option value="">Odaberite fond</option>{funds.filter((fund) => lines.some((source) => source.fundId === fund.id))
                .map((fund) => <option key={fund.id} value={fund.id}>{fund.name}</option>)}
            </select></label>
            <label>Opis <input value={line.description} maxLength={200} onChange={(event) => {
              requestId.current = ""; setExtraLines((current) => current.map((item) => item.key === line.key
                ? { ...item, description: event.target.value } : item)); }} /></label>
            <label>Iznos ({currencyCode}) <input type="number" min="0.01" step="0.01" value={line.amount}
              onChange={(event) => { requestId.current = ""; setExtraLines((current) => current.map((item) =>
                item.key === line.key ? { ...item, amount: event.target.value } : item)); }} /></label>
            <button type="button" onClick={() => { requestId.current = "";
              setExtraLines((current) => current.filter((item) => item.key !== line.key)); }}>Ukloni</button>
          </div>)}</>}
        <p><strong>UKUPNA KOREKCIJA: {formatMoney(signedTotal, currencyCode)}</strong></p>
        <button type="button" disabled={busy} onClick={() => void submit("corrections")}>KNJIŽI KOREKCIJU</button>
      </div>}
    </>}
  </section>;
}
