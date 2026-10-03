import { useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";
import { formatMoney } from "../currency.ts";

type ZevOption = { id: string; code: string; name: string };
type Summary = { billed: number | null; received: number | null; spent: number | null; netCash: number | null;
  currencyCode: string | null; mixedCurrency: boolean;
  balancesByCurrency: { currencyCode: string; billed: number; received: number;
    spent: number; netCash: number }[];
  fundMovements: { fundId: string; balance: number }[] };
type OpenItem = { id: string; personName: string; period: string; dueDate: string;
  reference: string; amount: number; allocated: number; outstanding: number;
  currencyCode: string; agingDays: number; agingBucket: string };
const agingLabels: Record<string, string> = { NotDue: "Nije dospjelo", Days1To30: "1–30 dana",
  Days31To60: "31–60 dana", Days61To90: "61–90 dana", Days91To180: "91–180 dana",
  Days181To365: "181–365 dana", Over365Days: "Preko 365 dana" };

export default function ReportsPage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [asOf, setAsOf] = useState("");
  const [personId, setPersonId] = useState("");
  const [summary, setSummary] = useState<Summary | null>(null);
  const [openItems, setOpenItems] = useState<Page<OpenItem>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:reports`);
  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    setLoading(true);
    try {
      const dates = new URLSearchParams();
      if (from) dates.set("from", from);
      if (to) dates.set("to", to);
      const [totals, items] = await Promise.all([
        callApi<Summary>(`/api/zevs/${zevId}/reports/summary?${dates}`, {}, token),
        callApi<Page<OpenItem>>(`/api/zevs/${zevId}/reports/open-items?page=${page}${asOf ? `&asOf=${asOf}` : ""}${personId ? `&personId=${encodeURIComponent(personId)}` : ""}`, {}, token),
      ]);
      if (currentRequest !== requestId.current) return;
      if (page > 1 && items.items.length === 0) { setPage(Math.max(1, Math.ceil(items.totalCount / 20))); return; }
      setSummary(totals); setOpenItems(items); setError("");
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
    finally { if (currentRequest === requestId.current) setLoading(false); }
  }, [zevId, from, to, asOf, personId, page, token]);
  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function download(path: string, fileName: string) {
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/reports/${path}`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Export failed");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = fileName;
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
  }

  return <>
    <SectionTitle title="Izvještaji" description="Prihodi, rashodi, novčani tok i otvorene stavke." />
    <div className="panel filter-row"><label>ZEV<select value={zevId} onChange={(event) => { setZevId(event.target.value); setPage(1); }}>
      {zevs.map((zev) => <option value={zev.id} key={zev.id}>{zev.name} ({zev.code})</option>)}</select></label>
      <label>Od<input type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
      <label>Do<input type="date" value={to} onChange={(event) => setTo(event.target.value)} /></label></div>
    <div className="panel filter-row"><label>Otvoreno na dan<input type="date" value={asOf} onChange={(event) => { setAsOf(event.target.value); setPage(1); }} /></label>
      <label>ID vlasnika<input value={personId} onChange={(event) => { setPersonId(event.target.value); setPage(1); }} placeholder="UUID vlasnika" /></label></div>
    <div className="panel filter-row"><label>Godina<input type="number" min="2000" max="2100" value={year}
      onChange={(event) => setYear(event.target.value)} /></label>
      {(!permissions || permissions.includes("Reports.Export")) && <button type="button"
        disabled={!zevId || summary?.mixedCurrency || Number(year) < 2000 || Number(year) > 2100}
        onClick={() => void download(`annual/${year}.pdf`, `godisnji-izvjestaj-${year}.pdf`)}>Godišnji PDF</button>}</div>
    {error && <p className="error" role="alert">{error}</p>}
    {loading && <p role="status">Učitavanje izvještaja…</p>}
    {summary?.mixedCurrency && <p className="panel" role="status">Jedinstveni zbir nije dostupan za više valuta
      ili nepoznatu istorijsku valutu. Iznosi su razdvojeni po valuti.</p>}
    {summary?.mixedCurrency && summary.balancesByCurrency.map((balance) => <div className="panel" key={balance.currencyCode}>
      <h2>{balance.currencyCode === "UNKNOWN" ? "Valuta nepoznata" : balance.currencyCode}</h2>
      <p>Zaduženo: {formatMoney(balance.billed, balance.currencyCode)} · Uplaćeno: {
        formatMoney(balance.received, balance.currencyCode)} · Rashodi: {
        formatMoney(balance.spent, balance.currencyCode)} · Neto tok: {
        formatMoney(balance.netCash, balance.currencyCode)}</p></div>)}
    {summary && !summary.mixedCurrency && <div className="finance-grid">
      <div className="panel"><h2>Zaduženo</h2><strong>{summary.currencyCode && summary.billed !== null
        ? formatMoney(summary.billed, summary.currencyCode) : "Valuta nije potvrđena"}</strong></div>
      <div className="panel"><h2>Uplaćeno</h2><strong>{summary.currencyCode && summary.received !== null
        ? formatMoney(summary.received, summary.currencyCode) : "Valuta nije potvrđena"}</strong></div>
      <div className="panel"><h2>Rashodi</h2><strong>{summary.currencyCode && summary.spent !== null
        ? formatMoney(summary.spent, summary.currencyCode) : "Valuta nije potvrđena"}</strong></div>
      <div className="panel"><h2>Neto tok</h2><strong>{summary.currencyCode && summary.netCash !== null
        ? formatMoney(summary.netCash, summary.currencyCode) : "Valuta nije potvrđena"}</strong></div>
    </div>}
    <section className="panel"><div className="admin-heading"><h2>Otvorene stavke</h2>
      {(!permissions || permissions.includes("Reports.Export")) && <button type="button" disabled={!zevId} onClick={() => void download(`open-items.xlsx?${new URLSearchParams({ ...(asOf ? { asOf } : {}), ...(personId ? { personId } : {}) })}`, "otvorene-stavke.xlsx")}>Izvezi XLSX</button>}</div>
      <div className="table-wrap"><table><thead><tr><th>Vlasnik</th><th>Period</th><th>Dospijeće</th>
        <th>Poziv na broj</th><th>Zaduženo</th><th>Uplaćeno</th><th>Otvoreno</th><th>Starost</th></tr></thead>
        <tbody>{openItems.items.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.period}</td>
          <td>{item.dueDate}</td><td>{item.reference}</td>
          <td>{formatMoney(item.amount, item.currencyCode)}</td>
          <td>{formatMoney(item.allocated, item.currencyCode)}</td>
          <td>{formatMoney(item.outstanding, item.currencyCode)}</td>
          <td>{agingLabels[item.agingBucket] ?? item.agingDays}</td></tr>)}</tbody></table></div>
      {openItems.totalCount === 0 && <p className="empty-inline">Nema otvorenih stavki.</p>}
      <Pager page={page} pageSize={openItems.pageSize} totalCount={openItems.totalCount} onPage={setPage} />
    </section>
  </>;
}
