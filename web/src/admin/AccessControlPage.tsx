import { FormEvent, useEffect, useState } from "react";
import { callApi, errorText, Page, Person, User } from "../api";
import { useChangeRevision } from "../useChangeRevision.ts";
import PermissionMatrix from "./PermissionMatrix";

type DerivedOwner = { zevId: string; propertyUnitId: string; unitCode: string; personId: string;
  personName: string; userId: string; userEmail: string; share: number; source: string };
type AccessOverride = { id: string; userId: string; permissionKey: string; effect: string;
  validFromUtc: string; validToUtc: string | null; reason: string; createdByUserId: string;
  revokedUtc: string | null; revokeReason: string | null };

export default function AccessControlPage({ token, zevId, zevs }: { token: string; zevId: string;
  zevs: { id: string; name: string }[] }) {
  const [people, setPeople] = useState<Person[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [owners, setOwners] = useState<DerivedOwner[]>([]);
  const [overrides, setOverrides] = useState<AccessOverride[]>([]);
  const [selectedPerson, setSelectedPerson] = useState("");
  const [selectedUser, setSelectedUser] = useState("");
  const [personSearch, setPersonSearch] = useState("");
  const [userSearch, setUserSearch] = useState("");
  const [personTotal, setPersonTotal] = useState(0);
  const [userTotal, setUserTotal] = useState(0);
  const [permission, setPermission] = useState("zev.read");
  const [effect, setEffect] = useState("Grant");
  const [validFrom, setValidFrom] = useState(() => new Date().toISOString().slice(0, 16));
  const [validTo, setValidTo] = useState("");
  const [reason, setReason] = useState("");
  const [revokeReason, setRevokeReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const revision = useChangeRevision("access", "people", "ownerships");

  useEffect(() => {
    let current = true;
    Promise.all([
      callApi<Page<Person>>(`/api/system/people?pageSize=100&search=${encodeURIComponent(personSearch)}`, {}, token),
      callApi<Page<User>>(`/api/system/users?pageSize=100&search=${encodeURIComponent(userSearch)}`, {}, token),
      callApi<Page<DerivedOwner>>(`/api/system/derived-owners?zevId=${zevId}&pageSize=100`, {}, token),
      callApi<AccessOverride[]>(`/api/zevs/${zevId}/access/overrides`, {}, token),
    ]).then(([personPage, userPage, ownerPage, accessRows]) => {
      if (!current) return;
      setPeople(personPage.items); setUsers(userPage.items); setOwners(ownerPage.items);
      setPersonTotal(personPage.totalCount); setUserTotal(userPage.totalCount);
      setSelectedPerson((selected) => personPage.items.some((person) => person.id === selected) ? selected : "");
      setSelectedUser((selected) => userPage.items.some((user) => user.id === selected) ? selected : "");
      setOverrides(accessRows); setError("");
    }).catch((problem) => { if (current) setError(errorText(problem)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [token, zevId, revision, personSearch, userSearch]);

  async function review(status: "Verified" | "Rejected" | "Revoked") {
    if (!selectedPerson || !reason.trim()) { setError("Odaberite osobu i unesite razlog."); return; }
    setBusy(true); setError(""); setMessage("");
    try {
      await callApi(`/api/system/people/${selectedPerson}/user-link/status`, {
        method: "POST", body: JSON.stringify({ status, reason,
          rowVersion: people.find((person) => person.id === selectedPerson)?.rowVersion })
      }, token);
      setMessage(`Veza je označena: ${status}.`); setReason("");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function createOverride(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(""); setMessage("");
    try {
      const result = await callApi<{ warning: string | null }>(`/api/zevs/${zevId}/access/overrides`, {
        method: "POST", body: JSON.stringify({ userId: selectedUser, permissionKey: permission,
          effect, validFromUtc: new Date(validFrom).toISOString(),
          validToUtc: validTo ? new Date(validTo).toISOString() : null, reason })
      }, token);
      setMessage(result.warning ?? "Ručni izuzetak je evidentiran."); setReason("");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function revoke(id: string) {
    setBusy(true); setError("");
    if (!revokeReason.trim()) { setError("Unesite razlog opoziva."); setBusy(false); return; }
    try { await callApi(`/api/zevs/${zevId}/access/overrides/${id}/revoke`, { method: "POST",
        body: JSON.stringify({ reason: revokeReason }) }, token);
      setMessage("Izuzetak je opozvan."); setRevokeReason(""); }
    catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <section className="panel">
    <h2>Automatski i ručni pristupi</h2>
    <p>Vlasnički pristup nastaje samo iz aktivnog vlasništva i verifikovane veze naloga s osobom. Ručni izuzetak nije vlasništvo.</p>
    {loading && <p>Učitavanje…</p>}
    {error && <p className="error" role="alert">{error}</p>}
    {message && <p role="status">{message}</p>}
    <PermissionMatrix token={token} zevId={zevId} zevs={zevs} users={users} selectedUser={selectedUser}
      onSelectUser={setSelectedUser} userSearch={userSearch} onUserSearch={setUserSearch} />
    <h3>Veza korisničkog naloga i osobe</h3>
    <label>Pretraga osobe <input type="search" value={personSearch}
      onChange={(event) => setPersonSearch(event.target.value)} placeholder="Ime ili email" /></label>
    {personTotal > 100 && <p>Prikazano prvih 100 od {personTotal}; suzite pretragu.</p>}
    <label>Osoba <select value={selectedPerson} onChange={(event) => setSelectedPerson(event.target.value)}>
      <option value="">Odaberite osobu</option>{people.filter((person) => person.userId).map((person) =>
        <option key={person.id} value={person.id}>{person.displayName} — {person.userLinkStatus}</option>)}</select></label>
    <label>Razlog <input value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} /></label>
    <div className="filter-row"><button type="button" disabled={busy} onClick={() => review("Verified")}>Potvrdi vezu</button>
      <button type="button" disabled={busy} onClick={() => review("Rejected")}>Odbij</button>
      <button type="button" disabled={busy} onClick={() => review("Revoked")}>Opozovi</button></div>
    <h3>Izvor: aktivno vlasništvo</h3>
    <div className="table-scroll"><table><thead><tr><th>Korisnik</th><th>Osoba</th><th>Jedinica</th><th>Udio</th><th>Izvor</th></tr></thead>
      <tbody>{owners.map((item) => <tr key={`${item.personId}-${item.propertyUnitId}`}>
        <td>{item.userEmail}</td><td>{item.personName}</td><td>{item.unitCode}</td><td>{item.share}</td><td>{item.source}</td>
      </tr>)}</tbody></table></div>
    {!loading && owners.length === 0 && <p className="empty-inline">Nema verifikovanih aktivnih vlasnika u ovom ZEV-u.</p>}
    <h3>Ručni izuzeci prava</h3>
    <form onSubmit={createOverride}>
      <label>Pretraga korisnika <input type="search" value={userSearch}
        onChange={(event) => setUserSearch(event.target.value)} placeholder="Email naloga" /></label>
      {userTotal > 100 && <p>Prikazano prvih 100 od {userTotal}; suzite pretragu.</p>}
      <label>Korisnik <select required value={selectedUser} onChange={(event) => setSelectedUser(event.target.value)}>
        <option value="">Odaberite korisnika</option>{users.filter((item) => item.isActive && !item.isSystemAdministrator).map((item) =>
          <option key={item.id} value={item.id}>{item.email}</option>)}</select></label>
      <label>Pravo <select value={permission} onChange={(event) => setPermission(event.target.value)}>
        <option value="zev.read">Pregled ZEV-a</option><option value="finance.manage">Upravljanje finansijama</option>
        <option value="legal.manage">Upravljanje registrom i pravnim predmetima</option>
        <option value="maintenance.manage">Upravljanje održavanjem</option></select></label>
      <label>Efekat <select value={effect} onChange={(event) => setEffect(event.target.value)}>
        <option value="Grant">Dozvoli</option><option value="Deny">Zabrani</option></select></label>
      <label>Od <input type="datetime-local" required value={validFrom}
        onChange={(event) => setValidFrom(event.target.value)} /></label>
      <label>Do <input type="datetime-local" value={validTo}
        onChange={(event) => setValidTo(event.target.value)} /></label>
      <p>Za korisnika bez aktivnog vlasništva, ručno pravo pregleda zahtijeva datum isteka. Ne otvara privatne podatke drugih osoba.</p>
      <button disabled={busy || !reason.trim() || !selectedUser}>Sačuvaj izuzetak</button>
    </form>
    <label>Razlog opoziva <input maxLength={500} value={revokeReason}
      onChange={(event) => setRevokeReason(event.target.value)} /></label>
    <div className="table-scroll"><table><thead><tr><th>Korisnik</th><th>Pravo</th><th>Efekat</th>
      <th>Od</th><th>Do</th><th>Razlog</th><th>Status</th><th>Akcija</th></tr></thead>
      <tbody>{overrides.map((item) => <tr key={item.id}><td>{users.find((user) => user.id === item.userId)?.email ?? item.userId}</td>
        <td>{item.permissionKey}</td><td>{item.effect}</td><td>{item.validFromUtc}</td><td>{item.validToUtc ?? "—"}</td>
        <td>{item.reason}</td><td>{item.revokedUtc ? `Opozvano${item.revokeReason ? `: ${item.revokeReason}` : ""}` : new Date(item.validToUtc ?? "9999-12-31") < new Date() ? "Isteklo" : "Aktivno"}</td>
        <td>{!item.revokedUtc && <button type="button" disabled={busy} onClick={() => void revoke(item.id)}>Opozovi</button>}</td>
      </tr>)}</tbody></table></div>
    {!loading && overrides.length === 0 && <p className="empty-inline">Nema ručnih izuzetaka.</p>}
  </section>;
}
