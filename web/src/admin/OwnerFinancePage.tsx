import { useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText } from "../api";
import { Pager, SectionTitle } from "./shared";
import InvoiceDocuments from "./InvoiceDocuments.tsx";
import { primaryInvoiceStatus } from "../invoiceLifecycleUi.ts";
import { useChangeRevision } from "../useChangeRevision.ts";
import { formatMoney, ownerLedgerLabel } from "../currency.ts";

type ZevOption = { id: string; code: string; name: string; historicalSelfAccess?: boolean };
type Card = { personId: string; totalCharged: number | null; totalPaid: number | null; balance: number | null;
  balances: { currencyCode: string; totalCharges: number; totalPayments: number;
    open: number; credit: number; netBalance: number }[];
  page: number; pageSize: number; totalCount: number;
  events: { date: string; kind: string; document: string; description: string;
    currencyCode: string; debt: number; payment: number; creditDelta: number;
    runningBalance: number; chargeId: string | null; paymentId: string | null;
    propertyUnitId: string | null; period: string | null }[];
  invoices: { id: string; propertyUnitId: string; invoiceNumber: string; period: string;
    issueDate: string; dueDate: string; amount: number; effectiveAmount: number;
    paid: number; open: number; correctionAmount: number; paidBeforeStorno: number; stornoCredit: number;
    paymentStatus: string; documentStatus: string; currency: string }[];
  documents: { id: string; originalInvoiceId: string; documentNumber: string; type: string;
    issueDate: string; signedAmount: number; reason: string; currency: string }[] };
type Unit = { id: string; zevId: string; code: string; type: string; areaSquareMeters: number };
type InvoiceDetail = { invoiceNumber: string; previousOpen: number; currentTotal: number;
  currency: string;
  totalDueAtIssue: number; currentPaid: number; totalOpen: number; credit: number; paymentStatus: string;
  documentStatus: string; effectiveTotal: number | null; paidBeforeStorno: number; stornoCredit: number;
  lines: { chargeId: string; name: string; amount: number; discountAmount: number }[] };

export default function OwnerFinancePage({ token, zevs }: { token: string; zevs: ZevOption[] }) {
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const historical = zevs.find((item) => item.id === zevId)?.historicalSelfAccess;
  const [cards, setCards] = useState<Card[]>([]);
  const [page, setPage] = useState(1);
  const [units, setUnits] = useState<Unit[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [openedInvoice, setOpenedInvoice] = useState<{ id: string; unitId: string;
    personId: string; period: string } | null>(null);
  const [invoiceDetail, setInvoiceDetail] = useState<InvoiceDetail | null>(null);
  const downloadPending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:owner`);
  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    setLoading(true);
    try {
      const [result, properties] = await Promise.all([
        callApi<Card[]>(`/api/me/finance/${zevId}?page=${page}&pageSize=20`, {}, token),
        callApi<Unit[]>("/api/me/property-units", {}, token),
      ]);
      if (currentRequest === requestId.current) {
        setCards(result); setUnits(properties.filter((unit) => unit.zevId === zevId)); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
    finally { if (currentRequest === requestId.current) setLoading(false); }
  }, [zevId, page, token]);
  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);
  useEffect(() => {
    if (!openedInvoice || !zevId) return;
    let active = true;
    void callApi<InvoiceDetail>(`/api/zevs/${zevId}/finance/invoices/${openedInvoice.unitId}/${openedInvoice.personId}?period=${openedInvoice.period}`,
      {}, token).then((result) => { if (active) setInvoiceDetail(result); })
      .catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [openedInvoice, zevId, token, revision]);
  async function downloadPdf(path: string, fileName: string) {
    if (downloadPending.current) return;
    downloadPending.current = true; setDownloading(true);
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}${path}`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("PDF nije dostupan.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = fileName;
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
    finally { downloadPending.current = false; setDownloading(false); }
  }
  return <>
    <SectionTitle eyebrow={null} title="Moj portal" description="Vaše nekretnine, zaduženja, uplate i saldo. Tuđi finansijski podaci nisu dostupni." />
    {historical && <p className="panel">Istorijski pristup: samo vaši finansijski zapisi iz perioda vlasništva. Novi ZEV sadržaj nije dostupan.</p>}
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => { setZevId(event.target.value); setPage(1); setOpenedInvoice(null); setInvoiceDetail(null); }}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}</select></label></div>
    {error && <p className="error" role="alert">{error}</p>}
    {loading && <p role="status">Učitavanje portala…</p>}
    <section className="panel"><h2>Moje nekretnine</h2>
      {units.length === 0 ? <p className="empty-inline">Nema povezanih nekretnina.</p>
        : units.map((unit) => <p key={unit.id}>{unit.code} · {unit.type} · {unit.areaSquareMeters} m²</p>)}</section>
    {cards.map((card) => <section className="panel" key={card.personId}>
      {card.balances.length === 0 && <p className="empty-inline">Nema finansijskih događaja.</p>}
      {card.balances.map((balance) => <div className="panel" key={balance.currencyCode}>
        <h3>Stanje · {balance.currencyCode}</h3><div className="finance-grid">
          <div>Zaduženo: <strong>{formatMoney(balance.totalCharges, balance.currencyCode)}</strong></div>
          <div>Uplaćeno: <strong>{formatMoney(balance.totalPayments, balance.currencyCode)}</strong></div>
          <div>Otvoreno: <strong>{formatMoney(balance.open, balance.currencyCode)}</strong></div>
          <div>Kredit: <strong>{formatMoney(balance.credit, balance.currencyCode)}</strong></div>
          <div>Neto saldo: <strong>{formatMoney(balance.netBalance, balance.currencyCode)}</strong></div>
        </div></div>)}
      <h2>Finansijski događaji</h2><div className="table-wrap"><table><thead><tr><th>Datum</th><th>Dokument</th>
        <th>Opis</th><th>Zaduženje</th><th>Uplata</th><th>Kredit ±</th><th>Saldo</th>
        <th>Dokument</th></tr></thead><tbody>
        {card.events.map((event, index) => <tr key={`${event.kind}-${event.chargeId ?? event.paymentId}-${index}`}>
          <td>{event.date}</td><td>{event.document}</td>
          <td>{ownerLedgerLabel(event.kind)} · {event.description}</td>
          <td>{formatMoney(event.debt, event.currencyCode)}</td>
          <td>{formatMoney(event.payment, event.currencyCode)}</td>
          <td>{formatMoney(event.creditDelta, event.currencyCode)}</td>
          <td>{formatMoney(event.runningBalance, event.currencyCode)}</td><td>
            {event.chargeId && event.debt > 0 && !historical && <button type="button" disabled={downloading}
              onClick={() => void downloadPdf(`/api/zevs/${zevId}/charges/${event.chargeId}/payment-slip.pdf`,
                `uplatnica-${event.chargeId}.pdf`)}>Uplatnica</button>}
            {event.kind === "LegacyCharge" && event.propertyUnitId && event.period && <button type="button"
              disabled={downloading} onClick={() => void downloadPdf(
                `/api/zevs/${zevId}/finance/invoices/${event.propertyUnitId}/${card.personId}.pdf?period=${event.period}`,
                `racun-${event.period}.pdf`)}>Račun</button>}
          </td></tr>)}</tbody></table></div>
      <Pager page={card.page} pageSize={card.pageSize} totalCount={card.totalCount} onPage={setPage} />
      <h2>Izdati računi</h2><div className="table-wrap"><table><thead><tr><th>Broj</th><th>Period</th>
        <th>Izdato</th><th>Dospijeće</th><th>Iznos pri izdavanju</th><th>Efektivno</th><th>Plaćeno</th><th>Otvoreno</th><th>Status</th><th>Dokument</th></tr></thead>
        <tbody>{card.invoices.map((invoice) => <tr key={invoice.id}><td>{invoice.invoiceNumber}</td>
          <td>{invoice.period}</td><td>{invoice.issueDate}</td><td>{invoice.dueDate}</td>
          <td>{formatMoney(invoice.amount, invoice.currency)}</td><td>{formatMoney(invoice.effectiveAmount, invoice.currency)}</td>
          <td>{formatMoney(invoice.paid, invoice.currency)}</td><td>{formatMoney(invoice.open, invoice.currency)}</td>
          <td><strong>{primaryInvoiceStatus(invoice.documentStatus, invoice.paymentStatus)}</strong>
            {invoice.documentStatus === "Corrected" && <div>Originalni iznos: {formatMoney(invoice.amount, invoice.currency)}<br />
              Korekcije: {formatMoney(invoice.correctionAmount, invoice.currency)}<br />
              Efektivno: {formatMoney(invoice.effectiveAmount, invoice.currency)}</div>}
            {invoice.documentStatus === "Cancelled" && <div>Originalni iznos: {formatMoney(invoice.amount, invoice.currency)}<br />
              Storno: {formatMoney(invoice.correctionAmount, invoice.currency)}<br />
              Otvoreno: {formatMoney(invoice.open, invoice.currency)}<br />
              Kredit nastao stornom: {formatMoney(invoice.stornoCredit, invoice.currency)}</div>}</td><td><button type="button" onClick={() => {
            setInvoiceDetail(null); setOpenedInvoice({ id: invoice.id, unitId: invoice.propertyUnitId, personId: card.personId,
              period: invoice.period }); }}>Otvori račun</button><button type="button" disabled={downloading}
            onClick={() => void downloadPdf(`/api/zevs/${zevId}/finance/invoices/${invoice.propertyUnitId}/${card.personId}.pdf?period=${invoice.period}`,
              `racun-${invoice.invoiceNumber}.pdf`)}>Pogledaj original</button>
            {card.documents.filter((document) => document.originalInvoiceId === invoice.id).map((document) =>
              <button type="button" key={document.id} disabled={downloading} onClick={() => void downloadPdf(
                `/api/zevs/${zevId}/finance/documents/${document.id}.pdf`, `${document.documentNumber}.pdf`)}>
                {document.type === "Storno" ? "Pogledaj storno" : "Pogledaj korekciju"} {document.documentNumber}</button>)}</td></tr>)}</tbody></table></div>
      {card.invoices.length === 0 && <p className="empty-inline">Nema izdatih računa. Stariji obračuni su u zaduženjima.</p>}
      <h3>Povezani dokumenti</h3>{card.documents.length === 0 ? <p className="empty-inline">Nema storna ili korekcija.</p>
        : <ul>{card.documents.map((document) => <li key={document.id}>{document.type} · {document.documentNumber}
          {" "}· {formatMoney(document.signedAmount, document.currency)} · {document.reason}
          <button type="button" disabled={downloading} onClick={() => void downloadPdf(
            `/api/zevs/${zevId}/finance/documents/${document.id}.pdf`, `${document.documentNumber}.pdf`)}>PDF</button>
        </li>)}</ul>}
    </section>)}
    {openedInvoice && invoiceDetail && <section className="panel"><h2>Račun {invoiceDetail.invoiceNumber}</h2>
      <p>Prethodni saldo: {formatMoney(invoiceDetail.previousOpen, invoiceDetail.currency)} · Novo zaduženje: {formatMoney(invoiceDetail.currentTotal, invoiceDetail.currency)} · Ukupno pri izdavanju: {formatMoney(invoiceDetail.totalDueAtIssue, invoiceDetail.currency)}</p>
      <ul>{invoiceDetail.lines.map((line) => <li key={line.chargeId}>{line.name} · popust {formatMoney(line.discountAmount, invoiceDetail.currency)} · {formatMoney(line.amount, invoiceDetail.currency)}</li>)}</ul>
      <h3>Trenutno stanje</h3>{invoiceDetail.documentStatus === "Cancelled"
        ? <p><strong>RAČUN STORNIRAN</strong> · Prije storna plaćeno: {formatMoney(invoiceDetail.paidBeforeStorno, invoiceDetail.currency)}
          {" "}· Nakon storna otvoreno: {formatMoney(invoiceDetail.totalOpen, invoiceDetail.currency)}
          {" "}· Kredit nastao stornom: {formatMoney(invoiceDetail.stornoCredit, invoiceDetail.currency)}
          {" "}· Trenutno neraspoređeno: {formatMoney(invoiceDetail.credit, invoiceDetail.currency)}</p>
        : <p>{invoiceDetail.documentStatus === "Corrected" ? "RAČUN KORIGOVAN · " : ""}
          Efektivno: {formatMoney(invoiceDetail.effectiveTotal ?? invoiceDetail.currentTotal, invoiceDetail.currency)}
          {" "}· Plaćeno: {formatMoney(invoiceDetail.currentPaid, invoiceDetail.currency)} · Otvoreno: {formatMoney(invoiceDetail.totalOpen, invoiceDetail.currency)}
          {" "}· Kredit: {formatMoney(invoiceDetail.credit, invoiceDetail.currency)} · {invoiceDetail.paymentStatus}</p>}
      <InvoiceDocuments zevId={zevId} invoiceId={openedInvoice.id} status={cards.flatMap((card) => card.invoices)
        .find((invoice) => invoice.id === openedInvoice.id)?.documentStatus ?? "Issued"} token={token}
        currencyCode={invoiceDetail.currency} />
    </section>}
  </>;
}
