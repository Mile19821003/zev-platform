import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";
import { formatMoney } from "../currency.ts";
import { label, legalCaseStatusLabels, reminderKindLabels } from "../uiLabels.ts";
import { apiBase } from "../apiBase.ts";

type ZevOption = { id: string; code: string; name: string };
type Person = { id: string; displayName: string };
type Policy = { id: string; validFrom: string; validTo: string | null; annualRatePercent: number; daysPerYear: number };
type Reminder = { id: string; personName: string; kind: string; asOf: string; principalSnapshot: number; interestSnapshot: number };
type LegalCase = { id: string; personId: string; personName: string; principalAmount: number; interestAmount: number;
  costsAmount: number; recoveredAmount: number; lawyer: string | null; court: string | null;
  caseNumber: string | null; filedOn: string | null; status: string; notes: string | null; rowVersion: string };

export default function LegalPage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [people, setPeople] = useState<Person[]>([]);
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [reminders, setReminders] = useState<Page<Reminder>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [cases, setCases] = useState<Page<LegalCase>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [casePage, setCasePage] = useState(1);
  const [personId, setPersonId] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [validTo, setValidTo] = useState("");
  const [rate, setRate] = useState("");
  const [daysPerYear, setDaysPerYear] = useState("");
  const [reminderKind, setReminderKind] = useState("Payment");
  const [asOf, setAsOf] = useState("");
  const [principal, setPrincipal] = useState("");
  const [interest, setInterest] = useState("");
  const [costs, setCosts] = useState("");
  const [recovered, setRecovered] = useState("");
  const [lawyer, setLawyer] = useState("");
  const [court, setCourt] = useState("");
  const [caseNumber, setCaseNumber] = useState("");
  const [filedOn, setFiledOn] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:legal`);

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const prefix = `/api/zevs/${zevId}/legal`;
      const [personData, policyData, reminderData, caseData] = await Promise.all([
        allowed("Persons.View") ? callApi<Page<Person>>(`${prefix}/people?pageSize=100`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
        callApi<Policy[]>(`${prefix}/interest-policies`, {}, token),
        callApi<Page<Reminder>>(`${prefix}/reminders`, {}, token),
        callApi<Page<LegalCase>>(`${prefix}/cases?page=${casePage}`, {}, token),
      ]);
      if (currentRequest === requestId.current) {
        if (casePage > 1 && caseData.items.length === 0) { setCasePage(Math.max(1, Math.ceil(caseData.totalCount / 20))); return; }
        setPeople(personData.items); setPolicies(policyData); setReminders(reminderData); setCases(caseData); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, casePage, token, permissions]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function submit(path: string, body: object, message: string) {
    if (busy) return;
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/legal/${path}`, { method: "POST", body: JSON.stringify(body) }, token);
      setNotice(message);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  function createPolicy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("interest-policies", { validFrom, validTo: validTo || null,
      annualRatePercent: Number(rate), daysPerYear: Number(daysPerYear) }, "Stopa je evidentirana.");
  }

  function createReminder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("reminders", { personId, kind: reminderKind, asOf }, "Opomena je evidentirana.");
  }

  async function downloadReminder(reminderId: string) {
    try {
      const response = await fetch(`${apiBase}/api/zevs/${zevId}/legal/reminders/${reminderId}.pdf`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("PDF nije dostupan.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = `opomena-${reminderId}.pdf`;
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
  }

  function createCase(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("cases", { personId, principalAmount: Number(principal),
      interestAmount: Number(interest || 0), costsAmount: Number(costs || 0), recoveredAmount: Number(recovered || 0),
      lawyer, court, caseNumber, filedOn: filedOn || null, notes }, "Pravni predmet je kreiran.");
  }

  async function updateCase(item: LegalCase, status: string) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      await callApi(`/api/zevs/${zevId}/legal/cases/${item.id}`, { method: "PUT", body: JSON.stringify({
        principalAmount: item.principalAmount, interestAmount: item.interestAmount, costsAmount: item.costsAmount,
        recoveredAmount: item.recoveredAmount, lawyer: item.lawyer, court: item.court,
        caseNumber: item.caseNumber, filedOn: item.filedOn, notes: item.notes, status, rowVersion: item.rowVersion,
      }) }, token);
      setNotice("Status predmeta je promijenjen.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <>
    <SectionTitle title="Opomene i pravni predmeti" description="Kamata je konfigurabilna; opomene čuvaju finansijski snapshot." />
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => setZevId(event.target.value)}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    {zevId && <><div className="finance-grid">
      <section className="panel"><h2>Stopa zatezne kamate</h2>
        {allowed("Legal.Edit") && <form className="admin-form" onSubmit={createPolicy}>
          <label>Od<input required type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></label>
          <label>Do (opcionalno)<input type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} /></label>
          <label>Godišnja stopa (%)<input required type="number" min="0" step="0.0001" value={rate} onChange={(event) => setRate(event.target.value)} /></label>
          <label>Broj dana u obračunskoj godini<input required type="number" min="1" max="366" value={daysPerYear}
            onChange={(event) => setDaysPerYear(event.target.value)} /></label>
          <button disabled={busy}>Sačuvaj stopu</button></form>}
        {policies.map((item) => <p key={item.id}>{item.validFrom} – {item.validTo ?? "dalje"}: {item.annualRatePercent}% / {item.daysPerYear} dana</p>)}
      </section>
      {allowed("Legal.Create") && <section className="panel"><h2>Nova opomena</h2><form className="admin-form" onSubmit={createReminder}>
        <label>Lice<select required value={personId} onChange={(event) => setPersonId(event.target.value)}>
          <option value="">Odaberite</option>{people.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
        <label>Vrsta<select value={reminderKind} onChange={(event) => setReminderKind(event.target.value)}>
          {Object.entries(reminderKindLabels).map(([code, name]) => <option key={code} value={code}>{name}</option>)}</select></label>
        <label>Stanje na dan<input required type="date" value={asOf} onChange={(event) => setAsOf(event.target.value)} /></label>
        <button disabled={busy}>Evidentiraj opomenu</button></form>
      </section>}
      {allowed("Legal.Create") && <section className="panel"><h2>Novi pravni predmet</h2><form className="admin-form" onSubmit={createCase}>
        <label>Lice<select required value={personId} onChange={(event) => setPersonId(event.target.value)}>
          <option value="">Odaberite</option>{people.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
        <label>Glavnica<input required type="number" min="0" step="0.01" value={principal} onChange={(event) => setPrincipal(event.target.value)} /></label>
        <label>Kamata<input type="number" min="0" step="0.01" value={interest} onChange={(event) => setInterest(event.target.value)} /></label>
        <label>Troškovi<input type="number" min="0" step="0.01" value={costs} onChange={(event) => setCosts(event.target.value)} /></label>
        <label>Naplaćeno<input type="number" min="0" step="0.01" value={recovered} onChange={(event) => setRecovered(event.target.value)} /></label>
        <label>Advokat<input maxLength={200} value={lawyer} onChange={(event) => setLawyer(event.target.value)} /></label>
        <label>Sud<input maxLength={200} value={court} onChange={(event) => setCourt(event.target.value)} /></label>
        <label>Broj predmeta<input maxLength={100} value={caseNumber} onChange={(event) => setCaseNumber(event.target.value)} /></label>
        <label>Datum predaje<input type="date" value={filedOn} onChange={(event) => setFiledOn(event.target.value)} /></label>
        <label>Napomena<input maxLength={2000} value={notes} onChange={(event) => setNotes(event.target.value)} /></label>
        <button disabled={busy}>Sačuvaj predmet</button></form>
      </section>}
    </div>
    <section className="panel table-panel"><h2>Opomene</h2><div className="table-scroll"><table><thead><tr>
      <th>Lice</th><th>Vrsta</th><th>Datum</th><th>Glavnica</th><th>Kamata</th><th>PDF</th>
    </tr></thead><tbody>{reminders.items.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{label(reminderKindLabels, item.kind)}</td>
      <td>{item.asOf}</td><td>{formatMoney(item.principalSnapshot, "UNKNOWN")}</td><td>{formatMoney(item.interestSnapshot, "UNKNOWN")}</td>
      <td>{allowed("Legal.Documents") && <button type="button" onClick={() => void downloadReminder(item.id)}>Preuzmi</button>}</td></tr>)}</tbody></table></div></section>
    <section className="panel table-panel"><h2>Pravni predmeti</h2><div className="table-scroll"><table><thead><tr>
      <th>Lice</th><th>Broj</th><th>Glavnica</th><th>Naplaćeno</th><th>Status</th>
    </tr></thead><tbody>{cases.items.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.caseNumber ?? "—"}</td>
      <td>{formatMoney(item.principalAmount, "UNKNOWN")}</td><td>{formatMoney(item.recoveredAmount, "UNKNOWN")}</td><td>{allowed("Legal.Edit") ? <select value={item.status}
        disabled={busy} onChange={(event) => updateCase(item, event.target.value)}>
          {Object.entries(legalCaseStatusLabels).map(([code, name]) => <option key={code} value={code}>{name}</option>)}</select>
          : label(legalCaseStatusLabels, item.status)}
      </td></tr>)}</tbody></table></div><Pager {...cases} onPage={setCasePage} /></section></>}
  </>;
}
