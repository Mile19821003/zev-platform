import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText } from "../api";
import ResourcePage, { ResourceConfig } from "./ResourcePage";
import { Lookup, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";

type ZevOption = { id: string; code: string; name: string };
type Section = "buildings" | "entrances" | "units" | "people" | "ownership";
type Ownership = { id: string; personName: string; validFrom: string; validTo: string | null; share: number };

function registryConfig(zevId: string, section: Exclude<Section, "ownership">): ResourceConfig {
  const prefix = `/api/zevs/${zevId}/registry`;
  const common = { allowEdit: true, search: true };
  if (section === "buildings") return { ...common, allowStatus: true,
    title: "Zgrade", description: "Zgrade u dodijeljenom ZEV-u.", path: `${prefix}/buildings`,
    filters: [{ key: "isActive", label: "Status", kind: "select", options: ["true", "false"] }],
    fields: [{ key: "code", label: "Šifra", required: true, maxLength: 50 },
      { key: "name", label: "Naziv", required: true, maxLength: 200 },
      { key: "addressLine", label: "Adresa", maxLength: 250 }],
    columns: [{ key: "code", label: "Šifra" }, { key: "name", label: "Naziv" }] };
  if (section === "entrances") return { ...common, allowStatus: true,
    title: "Ulazi", description: "Ulazi zgrada u dodijeljenom ZEV-u.", path: `${prefix}/entrances`,
    filters: [{ key: "buildingId", label: "Zgrada", kind: "lookup", lookupPath: () => `${prefix}/buildings` },
      { key: "isActive", label: "Status", kind: "select", options: ["true", "false"] }],
    fields: [{ key: "buildingId", label: "Zgrada", kind: "lookup", required: true,
      createOnly: true, lookupPath: () => `${prefix}/buildings` },
      { key: "code", label: "Šifra", required: true, maxLength: 50 },
      { key: "name", label: "Naziv", required: true, maxLength: 200 }],
    columns: [{ key: "code", label: "Šifra" }, { key: "name", label: "Naziv" },
      { key: "buildingName", label: "Zgrada" }] };
  if (section === "units") return { ...common, allowStatus: true,
    title: "Posebni dijelovi", description: "Stanovi, poslovni prostori i drugi posebni dijelovi.",
    path: `${prefix}/units`,
    filters: [{ key: "buildingId", label: "Zgrada", kind: "lookup", lookupPath: () => `${prefix}/buildings` },
      { key: "entranceId", label: "Ulaz", kind: "lookup", lookupPath: () => `${prefix}/entrances` },
      { key: "isActive", label: "Status", kind: "select", options: ["true", "false"] }],
    fields: [{ key: "entranceId", label: "Ulaz", kind: "lookup", required: true,
      createOnly: true, lookupPath: () => `${prefix}/entrances` },
      { key: "code", label: "Oznaka", required: true, maxLength: 50 },
      { key: "type", label: "Tip", kind: "select", required: true,
        options: ["Apartment", "Commercial", "Garage", "Basement", "Other"] },
      { key: "floor", label: "Sprat", kind: "number" },
      { key: "areaSquareMeters", label: "Površina (m²)", kind: "number", required: true, step: "0.001" },
      { key: "coefficient", label: "Koeficijent", kind: "number", required: true, step: "0.0001" },
      { key: "registeredMembers", label: "Prijavljeni članovi", kind: "number", required: true, step: "1" }],
    columns: [{ key: "code", label: "Oznaka" }, { key: "type", label: "Tip" },
      { key: "entranceName", label: "Ulaz" }, { key: "areaSquareMeters", label: "m²" }] };
  return { ...common, title: "Vlasnici / korisnici", description: "Fizička i pravna lica u ZEV-u.",
    path: `${prefix}/people`,
    filters: [{ key: "isActive", label: "Status", kind: "select", options: ["true", "false"] }],
    fields: [{ key: "kind", label: "Vrsta lica", kind: "select", required: true,
      options: ["Individual", "LegalEntity"] },
      { key: "displayName", label: "Ime / naziv", required: true, maxLength: 200 },
      { key: "taxIdentifier", label: "JIB / PIB", maxLength: 32 },
      { key: "addressLine", label: "Adresa", maxLength: 250 },
      { key: "cityMunicipality", label: "Grad / opština", maxLength: 120 },
      { key: "email", label: "Email", kind: "email", maxLength: 320 },
      { key: "phone", label: "Telefon", maxLength: 40 }],
    columns: [{ key: "displayName", label: "Ime / naziv" }, { key: "kind", label: "Vrsta" },
      { key: "email", label: "Email" }] };
}

export default function ManagerRegistryPage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [section, setSection] = useState<Section>("buildings");
  const [unitId, setUnitId] = useState("");
  const [personId, setPersonId] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [validTo, setValidTo] = useState("");
  const [share, setShare] = useState("1");
  const [ownerships, setOwnerships] = useState<Ownership[]>([]);
  const [closeDates, setCloseDates] = useState<Record<string, string>>({});
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const requestId = useRef(0);
  const prefix = `/api/zevs/${zevId}/registry`;
  const revision = useChangeRevision(`zev:${zevId}:ownerships`, `zev:${zevId}:units`);

  const loadOwnerships = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!unitId || !zevId) { setOwnerships([]); return; }
    setLoading(true);
    try {
      const result = await callApi<Ownership[]>(`${prefix}/units/${unitId}/ownerships`, {}, token);
      if (currentRequest === requestId.current) { setOwnerships(result); setError(""); }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
    finally { if (currentRequest === requestId.current) setLoading(false); }
  }, [prefix, token, unitId, zevId]);
  useEffect(() => { void loadOwnerships(); return () => { requestId.current++; }; }, [loadOwnerships, revision]);

  async function addOwnership(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`${prefix}/ownerships`, { method: "POST", body: JSON.stringify({
        propertyUnitId: unitId, personId, validFrom, validTo: validTo || null, share: Number(share),
      }) }, token);
      setNotice("Vlasništvo je evidentirano."); setPersonId("");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function closeOwnership(item: Ownership) {
    const date = closeDates[item.id];
    if (!date || !window.confirm("Zatvoriti ovaj period vlasništva?")) return;
    setBusy(true); setError("");
    try {
      await callApi(`${prefix}/ownerships/${item.id}/close`, {
        method: "POST", body: JSON.stringify({ validTo: date }),
      }, token);
      setNotice("Period je zatvoren.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <>
    <SectionTitle title="Registar zajednice" description="Upravljanje podacima samo u dodijeljenom ZEV-u." />
    <div className="panel filter-row"><label>ZEV<select value={zevId} onChange={(event) => {
      setZevId(event.target.value); setUnitId(""); setPersonId("");
    }}>{zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}</select></label></div>
    <div className="panel filter-row">{(["buildings", "entrances", "units", "people", "ownership"] as Section[])
      .map((item) => <button type="button" key={item} className={section === item ? "active" : "secondary"}
        onClick={() => setSection(item)}>{({ buildings: "Zgrade", entrances: "Ulazi", units: "Posebni dijelovi",
          people: "Vlasnici / korisnici", ownership: "Vlasništvo" })[item]}</button>)}</div>
    {zevId && section !== "ownership" && <ResourcePage key={`${zevId}-${section}`}
      config={{ ...registryConfig(zevId, section),
        allowCreate: allowed(({ buildings: "Buildings.Create", entrances: "Entrances.Create",
          units: "PropertyUnits.Create", people: "Persons.Create" })[section]),
        allowEdit: allowed(({ buildings: "Buildings.Edit", entrances: "Entrances.Edit",
          units: "PropertyUnits.Edit", people: "Persons.Edit" })[section]),
        allowStatus: allowed(({ buildings: "Buildings.Edit", entrances: "Entrances.Edit",
          units: "PropertyUnits.Edit", people: "Persons.Edit" })[section]) }} token={token} onChanged={() => undefined}
      onCreated={section === "people" ? (id) => { setPersonId(id); setSection("ownership");
        setNotice("Lice je kreirano. Odaberite posebni dio i evidentirajte vlasništvo."); } : undefined} />}
    {zevId && section === "ownership" && <>
      <div className="panel unit-picker"><Lookup token={token} label="Posebni dio" path={`${prefix}/units`}
        value={unitId} onChange={setUnitId} /></div>
      {error && <p role="alert" className="error">{error}</p>}
      {notice && <p role="status" className="notice">{notice}</p>}
      {unitId && <div className="admin-layout">{allowed("Ownership.Create") && <section className="panel"><h2>Novi period vlasništva</h2>
        <form className="admin-form" onSubmit={addOwnership}>
          <Lookup token={token} label="Postojeći vlasnik" path={`${prefix}/people`}
            value={personId} onChange={setPersonId} />
          <label>ID novog lica<input required value={personId} onChange={(event) => setPersonId(event.target.value)}
            placeholder="Odaberite postojeće ili koristite upravo kreirano lice" /></label>
          <label>Od datuma<input required type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></label>
          <label>Do datuma<input type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} /></label>
          <label>Udio<input required type="number" min="0.000001" max="1" step="0.000001" value={share}
            onChange={(event) => setShare(event.target.value)} /></label>
          <button type="submit" disabled={busy || !personId}>Evidentiraj</button>
        </form></section>}<section className="panel table-panel"><h2>Istorija vlasništva</h2>
          {loading && <p role="status">Učitavanje…</p>}
          <div className="table-wrap"><table><thead><tr><th>Vlasnik</th><th>Od</th><th>Do</th><th>Udio</th><th>Radnja</th></tr></thead>
            <tbody>{ownerships.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.validFrom}</td>
              <td>{item.validTo ?? "Aktivno"}</td><td>{(item.share * 100).toFixed(2)}%</td><td>{allowed("Ownership.Close") && !item.validTo && <>
                <input aria-label="Datum zatvaranja" type="date" value={closeDates[item.id] ?? ""}
                  onChange={(event) => setCloseDates((current) => ({ ...current, [item.id]: event.target.value }))} />
                <button type="button" disabled={busy} onClick={() => void closeOwnership(item)}>Zatvori</button></>}</td></tr>)}</tbody>
          </table></div>{!loading && ownerships.length === 0 && <p className="empty-inline">Nema vlasništva.</p>}
        </section></div>}
    </>}
  </>;
}
