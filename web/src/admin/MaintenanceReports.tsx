import { useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { downloadProtectedFile } from "../authBlob.ts";
import { formatDate, formatNumber } from "../displaySettings.ts";
import { filterRows, frequencyText, MaintenanceReport, monthBars, monthNames, percent, pieSlices, reportExportPath,
  ReportView, reportViews, sourceLabels, timelinePosition } from "../maintenanceReports.ts";
import { scheduleBadgeClass, scheduleStatusLabel } from "../maintenanceCalendar.ts";
import { label, maintenanceStatusLabels } from "../uiLabels.ts";
import { useChangeRevision } from "../useChangeRevision.ts";

const statusText = (status: string) => ["Planned", "Due"].includes(status) ? scheduleStatusLabel(status) : label(maintenanceStatusLabels, status);
const money = (value: number) => `${formatNumber(value)} KM`;

// Reporting centre: analytical table with PDF / Excel export, charts and the season timeline of one year.
export default function MaintenanceReports({ token, zevId, canExport }: { token: string; zevId: string; canExport: boolean }) {
  const thisYear = new Date().getFullYear();
  const [year, setYear] = useState(thisYear);
  const [view, setView] = useState<ReportView>("table");
  const [report, setReport] = useState<MaintenanceReport | null>(null);
  const [source, setSource] = useState("");
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState("");
  const revision = useChangeRevision("maintenance", `zev:${zevId}:maintenance`);

  useEffect(() => {
    let current = true;
    callApi<MaintenanceReport>(`/api/zevs/${zevId}/maintenance/reports/summary?year=${year}`, {}, token)
      .then((result) => { if (current) { setReport(result); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, zevId, year, revision]);

  async function exportReport(format: "pdf" | "xlsx") {
    setBusy(format); setError("");
    try { await downloadProtectedFile(reportExportPath(zevId, year, format), token, `odrzavanje-${year}.${format}`); }
    catch (problem) { setError(problem instanceof Error ? problem.message : errorText(problem)); }
    finally { setBusy(""); }
  }

  const rows = report ? filterRows(report.rows, source, search) : [];
  const slices = report ? pieSlices(report.costByCategory) : [];
  const bars = report ? monthBars(report.months) : [];
  return <section className="panel maintenance-reports" aria-label="Izvještaji održavanja">
    <div className="reports-head"><h2>Izvještaji održavanja</h2>
      <label>Godina<select value={year} onChange={(event) => setYear(Number(event.target.value))}>
        {[thisYear + 1, thisYear, thisYear - 1, thisYear - 2].map((item) => <option key={item} value={item}>{item}</option>)}</select></label>
      {canExport && <div className="reports-export">
        <button type="button" disabled={busy !== "" || !report} onClick={() => void exportReport("pdf")}>{busy === "pdf" ? "Izvoz…" : "Izvezi u PDF"}</button>
        <button type="button" disabled={busy !== "" || !report} onClick={() => void exportReport("xlsx")}>{busy === "xlsx" ? "Izvoz…" : "Izvezi u Excel"}</button></div>}
    </div>
    <div className="filter-tabs report-tabs" role="tablist" aria-label="Prikaz izvještaja">{reportViews.map((item) =>
      <button type="button" role="tab" key={item.value} aria-selected={view === item.value} className={view === item.value ? "active" : ""}
        onClick={() => setView(item.value)}>{item.label}</button>)}</div>
    {error && <p className="error" role="alert">{error}</p>}
    {report && <div className="report-totals">
      <div><span>Planirano</span><strong>{report.planned}</strong></div><div><span>Realizovano</span><strong>{report.realized}</strong></div>
      <div><span>Troškovi</span><strong>{money(report.totalCost)}</strong></div></div>}

    {report && view === "table" && <div role="tabpanel" aria-label="Analitička tabela">
      <div className="report-filters"><label>Izvor<select value={source} onChange={(event) => setSource(event.target.value)}>
        <option value="">Svi</option>{Object.entries(sourceLabels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select></label>
        <label>Pretraga<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="posao, kategorija, izvođač" /></label></div>
      <div className="table-scroll"><table className="report-table"><thead><tr><th>Izvor</th><th>Posao</th><th>Kategorija</th><th>Planirano</th>
        <th>Realizovano</th><th>Status</th><th>Izvođač</th><th>Račun</th><th>Iznos</th></tr></thead>
        <tbody>{rows.map((row, index) => <tr key={index} className={row.projected ? "projected" : ""}>
          <td>{sourceLabels[row.source] ?? row.source}</td><td>{row.title}{row.projected && <small className="projected-note"> (projekcija)</small>}</td>
          <td>{row.category}</td><td>{row.plannedOn ? formatDate(row.plannedOn) : "—"}</td><td>{row.completedOn ? formatDate(row.completedOn) : "—"}</td>
          <td><span className={scheduleBadgeClass(row.status)}>{statusText(row.status)}</span></td><td>{row.contractor ?? "—"}</td>
          <td>{row.invoiceNumber ?? "—"}</td><td className="number">{row.cost === null ? "—" : money(row.cost)}</td></tr>)}</tbody>
        <tfoot><tr><td colSpan={8}>Ukupno ({rows.length} {rows.length === 1 ? "stavka" : "stavki"})</td>
          <td className="number">{money(rows.reduce((sum, row) => sum + (row.cost ?? 0), 0))}</td></tr></tfoot></table></div>
      {rows.length === 0 && <p className="empty-inline">Nema poslova za odabranu godinu i filtere.</p>}</div>}

    {report && view === "charts" && <div role="tabpanel" aria-label="Grafikoni" className="report-charts">
      <figure className="chart"><figcaption>Udio troškova po kategorijama</figcaption>
        {slices.length === 0 ? <p className="empty-inline">Nema evidentiranih troškova.</p> : <div className="pie-wrap">
          <svg viewBox="0 0 180 180" width="180" height="180" role="img" aria-label="Pita grafikon troškova po kategorijama">
            {slices.map((slice) => <path key={slice.category} d={slice.path} fill={slice.color} stroke="var(--zev-surface)" strokeWidth="1.5">
              <title>{slice.category}: {money(slice.cost)} ({percent(slice.share)})</title></path>)}</svg>
          <ul className="chart-legend">{slices.map((slice) => <li key={slice.category}><span style={{ background: slice.color }} />
            {slice.category} — {money(slice.cost)} ({percent(slice.share)})</li>)}</ul></div>}</figure>
      <figure className="chart"><figcaption>Realizovano naspram planiranog po mjesecima</figcaption>
        <svg viewBox="0 0 600 220" className="bar-chart" role="img" aria-label="Stubičasti grafikon: planirano i realizovano po mjesecima">
          {[0, 0.5, 1].map((level) => <line key={level} x1="30" x2="590" y1={190 - level * 160} y2={190 - level * 160} className="grid-line" />)}
          <text x="24" y="34" className="axis-label" textAnchor="end">{bars[0]?.max ?? 1}</text>
          {bars.map((bar, index) => {
            const x = 40 + index * 46;
            return <g key={bar.month}>
              <rect x={x} y={190 - bar.planned * 160} width="16" height={bar.planned * 160} className="bar planned"><title>{monthNames[index]}: planirano {report.months[index].planned}</title></rect>
              <rect x={x + 18} y={190 - bar.realized * 160} width="16" height={bar.realized * 160} className="bar realized"><title>{monthNames[index]}: realizovano {report.months[index].realized}</title></rect>
              <text x={x + 17} y="208" className="axis-label" textAnchor="middle">{monthNames[index]}</text></g>;
          })}</svg>
        <ul className="chart-legend inline"><li><span className="planned" />Planirano</li><li><span className="realized" />Realizovano</li></ul></figure>
    </div>}

    {report && view === "timeline" && <div role="tabpanel" aria-label="Gantogram" className="gantt">
      <div className="gantt-row gantt-header"><span className="gantt-label">Plan</span><div className="gantt-track">
        {monthNames.map((name) => <span key={name} className="gantt-month">{name}</span>)}</div></div>
      {report.gantt.map((plan) => <div key={plan.planId} className={`gantt-row${plan.isActive ? "" : " inactive"}`}>
        <span className="gantt-label"><strong>{plan.title}</strong><small>{plan.category}{plan.frequencyDays ? ` · ${frequencyText(plan.frequencyDays)}` : ""}</small></span>
        <div className="gantt-track">{monthNames.map((name) => <span key={name} className="gantt-cell" />)}
          {plan.marks.map((mark) => <span key={mark.date} className={`gantt-mark status-${mark.status.toLowerCase()}${mark.projected ? " projected" : ""}`}
            style={{ left: `${timelinePosition(mark.date, report.year)}%` }} title={`${formatDate(mark.date)} · ${statusText(mark.status)}${mark.projected ? " (projekcija)" : ""}`} />)}</div>
      </div>)}
      {report.gantt.length === 0 && <p className="empty-inline">Nema planova održavanja za ovu godinu.</p>}
      <ul className="chart-legend inline"><li><span className="mark completed" />Završeno</li><li><span className="mark due" />Dospjelo</li>
        <li><span className="mark planned" />Planirano</li><li><span className="mark projected" />Projekcija</li></ul>
    </div>}
  </section>;
}
