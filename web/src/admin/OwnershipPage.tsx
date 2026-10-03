import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Ownership, PropertyUnit } from "../api";
import { Lookup, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";

type Occupancy = { id: string; personName: string; kind: string; validFrom: string; validTo: string | null };

export default function OwnershipPage({ token }: { token: string }) {
  const [unitId, setUnitId] = useState("");
  const [unit, setUnit] = useState<PropertyUnit | null>(null);
  const [personId, setPersonId] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [validTo, setValidTo] = useState("");
  const [share, setShare] = useState("1");
  const [occupantKind, setOccupantKind] = useState("Tenant");
  const [ownerships, setOwnerships] = useState<Ownership[]>([]);
  const [occupancies, setOccupancies] = useState<Occupancy[]>([]);
  const [closeDates, setCloseDates] = useState<Record<string, string>>({});
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const requestId = useRef(0);
  const revision = useChangeRevision("ownerships", "occupancies", "propertyUnits");

  const load = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!unitId) { setUnit(null); setOwnerships([]); setOccupancies([]); return; }
    try {
      const [selected, owners, occupants] = await Promise.all([
        callApi<PropertyUnit>(`/api/system/property-units/${unitId}`, {}, token),
        callApi<Ownership[]>(`/api/system/property-units/${unitId}/ownerships`, {}, token),
        callApi<Occupancy[]>(`/api/system/property-units/${unitId}/occupancies`, {}, token),
      ]);
      if (currentRequest === requestId.current) {
        setUnit(selected); setOwnerships(owners); setOccupancies(occupants); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [token, unitId]);

  useEffect(() => { void load(); return () => { requestId.current++; }; }, [load, revision]);

  async function addOwnership(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi("/api/system/ownerships", { method: "POST", body: JSON.stringify({
        propertyUnitId: unitId, personId, validFrom, validTo: validTo || null, share: Number(share),
      }) }, token);
      setNotice("Vlasništvo je evidentirano."); setPersonId(""); setValidFrom(""); setValidTo("");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function closeOwnership(ownership: Ownership) {
    const date = closeDates[ownership.id];
    if (!date) { setError("Unesite datum završetka vlasništva."); return; }
    setBusy(true); setError("");
    try {
      await callApi(`/api/system/ownerships/${ownership.id}/close`, {
        method: "POST", body: JSON.stringify({ validTo: date }),
      }, token);
      setNotice("Period vlasništva je zatvoren.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function addOccupancy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true); setError(""); setNotice("");
    try {
      await callApi("/api/system/occupancies", { method: "POST", body: JSON.stringify({
        propertyUnitId: unitId, personId, kind: occupantKind, validFrom, validTo: validTo || null,
      }) }, token);
      setNotice("Korisnik nekretnine je evidentiran.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <>
    <SectionTitle title="Vlasništvo i korisnici" description="Periodi vlasništva ostaju sačuvani nakon promjene vlasnika." />
    <div className="panel unit-picker"><Lookup token={token} label="Posebni dio" path="/api/system/property-units"
      value={unitId} onChange={(value) => { setUnitId(value); setPersonId(""); }} /></div>
    {error && <p className="error" role="alert">{error}</p>}
    {notice && <p className="notice" role="status">{notice}</p>}
    {unit && <>
      <div className="admin-layout"><section className="panel"><h2>Novi period vlasništva</h2>
        <form className="admin-form" onSubmit={addOwnership}>
          <Lookup token={token} label="Vlasnik" path={`/api/system/people?organizationId=${unit.organizationId}`}
            value={personId} onChange={setPersonId} />
          <label>Od datuma<input required type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></label>
          <label>Do datuma (opcionalno)<input type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} /></label>
          <label>Vlasnički udio<input required type="number" min="0.000001" max="1" step="0.000001"
            value={share} onChange={(event) => setShare(event.target.value)} /></label>
          <button type="submit" disabled={busy || !personId}>Evidentiraj vlasništvo</button>
        </form></section>
        <section className="panel table-panel"><h2>Istorija vlasništva · {unit.code}</h2>
          <div className="table-scroll"><table><thead><tr><th>Vlasnik</th><th>Od</th><th>Do</th><th>Udio</th><th>Radnja</th></tr></thead>
            <tbody>{ownerships.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.validFrom}</td>
              <td>{item.validTo ?? "Aktivno"}</td><td>{item.share}</td><td>{!item.validTo && <div className="inline-action">
                <input aria-label="Datum zatvaranja" type="date" value={closeDates[item.id] ?? ""}
                  onChange={(event) => setCloseDates((current) => ({ ...current, [item.id]: event.target.value }))} />
                <button type="button" disabled={busy} onClick={() => closeOwnership(item)}>Zatvori</button></div>}</td></tr>)}</tbody></table></div>
          {ownerships.length === 0 && <p className="empty-inline">Nema evidentiranih vlasnika.</p>}
        </section></div>
      <div className="admin-layout"><section className="panel"><h2>Korisnik / podstanar</h2>
        <form className="admin-form" onSubmit={addOccupancy}>
          <Lookup token={token} label="Lice" path={`/api/system/people?organizationId=${unit.organizationId}`}
            value={personId} onChange={setPersonId} />
          <label>Vrsta<select value={occupantKind} onChange={(event) => setOccupantKind(event.target.value)}>
            <option value="Resident">Stanovnik</option><option value="Tenant">Podstanar</option>
            <option value="OtherUser">Drugi korisnik</option></select></label>
          <label>Od datuma<input required type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></label>
          <label>Do datuma (opcionalno)<input type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} /></label>
          <button type="submit" disabled={busy || !personId}>Evidentiraj korisnika</button>
        </form></section>
        <section className="panel table-panel"><h2>Istorija korištenja</h2>
          <div className="table-scroll"><table><thead><tr><th>Lice</th><th>Vrsta</th><th>Od</th><th>Do</th></tr></thead>
            <tbody>{occupancies.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.kind}</td>
              <td>{item.validFrom}</td><td>{item.validTo ?? "Aktivno"}</td></tr>)}</tbody></table></div>
          {occupancies.length === 0 && <p className="empty-inline">Nema evidentiranih korisnika.</p>}
        </section></div>
    </>}
  </>;
}
