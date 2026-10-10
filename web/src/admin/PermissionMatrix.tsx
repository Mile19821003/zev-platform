import { Fragment, useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { useChangeRevision } from "../useChangeRevision.ts";
import "../permissions.css";
import { cellState, cellView, columnLabels as labels, Effect, MatrixModule as Module, nextEffect, pendingChanges,
  PermissionState, visibleColumns } from "../permissionMatrixUi.ts";

type Context = { roles: string[]; isOwner: boolean; sources: string[]; permissions: PermissionState[] };
export type PermissionUser = { id: string; email: string; isActive: boolean; isSystemAdministrator: boolean };

export default function PermissionMatrix({ token, zevId, zevs, users, selectedUser, onSelectUser,
  userSearch, onUserSearch }: { token: string; zevId: string; zevs: { id: string; name: string }[];
  users: PermissionUser[]; selectedUser: string; onSelectUser: (id: string) => void; userSearch: string;
  onUserSearch: (value: string) => void }) {
  const [context, setContext] = useState<Context | null>(null);
  const [modules, setModules] = useState<Module[]>([]);
  const [draft, setDraft] = useState<Record<string, Effect>>({});
  const [expanded, setExpanded] = useState<string[]>([]);
  const [reason, setReason] = useState("");
  const [sourceUser, setSourceUser] = useState("");
  const [sourceZev, setSourceZev] = useState(zevId);
  const [sourceUsers, setSourceUsers] = useState<PermissionUser[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const revision = useChangeRevision("access");

  useEffect(() => { setSourceZev(zevId); }, [zevId]);
  useEffect(() => {
    let current = true;
    setSourceUser("");
    callApi<PermissionUser[]>(`/api/zevs/${sourceZev}/access/users`, {}, token)
      .then((items) => { if (current) setSourceUsers(items); })
      .catch(() => { if (current) setSourceUsers([]); });
    return () => { current = false; };
  }, [sourceZev, token]);
  useEffect(() => {
    let current = true;
    setContext(null); setDraft({});
    callApi<{ modules: Module[] }>(`/api/zevs/${zevId}/access/permissions`, {}, token)
      .then((result) => { if (current) setModules(result.modules); })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    if (selectedUser) callApi<Context>(`/api/zevs/${zevId}/access/users/${selectedUser}/permissions`, {}, token)
      .then((result) => { if (current) { setContext(result); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, zevId, selectedUser, revision]);

  const columns = visibleColumns(modules);
  const roleName = context ? context.roles.join(", ") || (context.isOwner ? "Owner" : "nema") : "";
  const changes = context ? pendingChanges(context.permissions, draft) : [];
  function state(key: string) {
    return cellState(context?.permissions.find((item) => item.key === key), draft, key);
  }
  function setKeys(keys: string[], effect: Effect) {
    setMessage("");
    setDraft((current) => Object.fromEntries([...Object.entries(current), ...keys.map((key) => [key, effect])]) as Record<string, Effect>);
  }
  function toggle(key: string) { setKeys([key], nextEffect(state(key))); }
  function cell(key: string) {
    if (!context?.permissions.some((item) => item.key === key)) return <span aria-label="Nije dostupno">—</span>;
    const current = state(key);
    const view = cellView(current, roleName);
    return <button type="button" className={`permission-cell ${view.className}`}
      title={view.label} aria-label={`${key}: ${view.label}`} aria-pressed={current.effective} onClick={() => toggle(key)}>
      {view.glyph}</button>;
  }

  async function save() {
    if (!selectedUser || !reason.trim() || changes.length === 0) {
      setError("Odaberite korisnika, promijenite prava i unesite razlog."); return;
    }
    setBusy(true); setError(""); setMessage("");
    try {
      const result = await callApi<Context>(`/api/zevs/${zevId}/access/users/${selectedUser}/permissions`, {
        method: "PUT", body: JSON.stringify({ reason, changes }),
      }, token);
      setContext(result); setDraft({}); setReason(""); setMessage("Prava su sačuvana i auditirana.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  async function copy() {
    if (!sourceUser || !sourceZev || !selectedUser) { setError("Odaberite izvorni korisnik, izvorni ZEV i ciljnog korisnika."); return; }
    setBusy(true); setError("");
    try {
      const source = await callApi<Context>(`/api/zevs/${sourceZev}/access/users/${sourceUser}/permissions`, {}, token);
      setDraft(Object.fromEntries(source.permissions.flatMap((item) => {
        const target = context?.permissions.find((permission) => permission.key === item.key);
        if (!target) return [];
        const effect = item.effective === target.roleDefault ? null : item.effective ? "Grant" : "Deny";
        return effect === target.override ? [] : [[item.key, effect]];
      })) as Record<string, Effect>);
      setMessage("Prava su kopirana u nacrt. Pregledajte i snimite za ciljni ZEV.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <section className="panel permission-matrix"><h3>Prava · tabelarna matrica</h3>
    <div className="filter-row"><label>Pretraga korisnika <input type="search" value={userSearch}
      onChange={(event) => onUserSearch(event.target.value)} placeholder="Email naloga" /></label>
      <label>Korisnik <select value={selectedUser} onChange={(event) => onSelectUser(event.target.value)}>
        <option value="">Odaberite korisnika</option>{users.filter((item) => item.isActive && !item.isSystemAdministrator)
          .map((item) => <option key={item.id} value={item.id}>{item.email}</option>)}</select></label>
      <span>ZEV: {zevs.find((item) => item.id === zevId)?.name ?? zevId}</span></div>
    {context && <><p>Uloga: {context.roles.join(", ") || (context.isOwner ? "Owner (vlasništvo)" : "Bez članstva")}
      · Izvor: {context.sources.join(", ")} · Role default + override</p>
      <div className="filter-row"><button type="button" title="Uklanja sve ručne dozvole i zabrane u nacrtu; snimanje ih trajno uklanja"
        onClick={() => setKeys(context.permissions.filter((item) => item.override !== null).map((item) => item.key), null)}>
        Vrati sve na default uloge</button>{changes.length > 0 && <span>Nesačuvanih promjena: {changes.length}</span>}
        {Object.keys(draft).length > 0 && <button type="button" onClick={() => setDraft({})}>Odbaci nacrt</button>}</div>
      <ul className="permission-legend" aria-label="Legenda">
        <li><span className="permission-cell inherited">✓</span> naslijeđeno iz uloge</li>
        <li><span className="permission-cell granted">✓+</span> ručno dozvoljeno (GRANT)</li>
        <li><span className="permission-cell denied">✕</span> ručno zabranjeno (DENY)</li>
        <li><span className="permission-cell none">□</span> nije dozvoljeno</li>
        <li>Klik: default → suprotan ručni izuzetak → nazad na default. Isprekidan okvir = nesačuvano.</li></ul>
      <div className="table-scroll"><table><thead><tr><th>Modul</th>{columns.map((column) => <th key={column}>
        {labels[column]}<button type="button" title={`Označi kolonu ${labels[column]}`}
          onClick={() => setKeys(modules.filter((module) => module.actions.includes(column))
            .map((module) => `${module.name}.${column}`), "Grant")}>+</button></th>)}</tr></thead>
        <tbody>{modules.map((module) => <Fragment key={module.name}><tr><th><button type="button" aria-label={`Detalji ${module.label}`}
          onClick={() => setExpanded((current) => current.includes(module.name) ? current.filter((item) => item !== module.name) : [...current, module.name])}>
          {expanded.includes(module.name) ? "−" : "+"}</button> {module.label}
          <button type="button" title="Označi cijeli red" onClick={() => setKeys([...module.actions.map((action) => `${module.name}.${action}`), ...module.details], "Grant")}>✓</button>
          <button type="button" title="Ukloni cijeli red" onClick={() => setKeys([...module.actions.map((action) => `${module.name}.${action}`), ...module.details], "Deny")}>✕</button>
          <button type="button" title="Resetuj modul na default" onClick={() => setKeys([...module.actions.map((action) => `${module.name}.${action}`), ...module.details], null)}>↺</button>
        </th>{columns.map((column) => <td key={column}>{module.actions.includes(column) ? cell(`${module.name}.${column}`) : "—"}</td>)}</tr>
          {expanded.includes(module.name) && module.details.map((key) => <tr key={key}><td>{key}</td><td>{cell(key)}</td>
            <td colSpan={Math.max(columns.length - 1, 1)}>Posebna akcija · {state(key).effect ?? "default uloge"}</td></tr>)}</Fragment>)}</tbody></table></div>
      <div className="filter-row"><label>Razlog promjene <input maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} /></label>
        <button type="button" disabled={busy || changes.length === 0} onClick={() => void save()}>Snimi prava</button></div>
      <details><summary>Kopiraj prava sa korisnika</summary><div className="filter-row">
        <label>Izvorni korisnik <select value={sourceUser} onChange={(event) => setSourceUser(event.target.value)}>
          <option value="">Odaberite</option>{sourceUsers.filter((item) => item.isActive && !item.isSystemAdministrator)
            .map((item) => <option key={item.id} value={item.id}>{item.email}</option>)}</select></label>
        <label>Izvorni ZEV <select value={sourceZev} onChange={(event) => setSourceZev(event.target.value)}>
          {zevs.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <button type="button" disabled={busy} onClick={() => void copy()}>Kopiraj u nacrt</button></div>
        <p>Cilj: {users.find((item) => item.id === selectedUser)?.email} · {zevs.find((item) => item.id === zevId)?.name}</p></details>
    </>}
    {error && <p className="error" role="alert">{error}</p>}{message && <p role="status">{message}</p>}
  </section>;
}
