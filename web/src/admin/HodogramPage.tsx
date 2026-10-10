import { FormEvent, Fragment, useCallback, useEffect, useRef, useState } from "react";
import { ApiError, callApi, errorText } from "../api";
import { draftFromDetail, draftIssues, HodogramDetail, HodogramStep, HodogramSummary, isDraftChanged,
  issuesByStep, MatrixDraft, matrixPayload, RaciIssue, raciLabels, RaciValue, raciValues, roleLabel } from "../hodogramUi.ts";
import { Lookup, SectionTitle, Status } from "./shared";
import "../hodogram.css";

type OrganizationOption = { id: string; name: string; readOnly?: boolean };
type StepEdit = { id: string; name: string; description: string; phaseId: string; sortOrder: string };

export default function HodogramPage({ token, organizations }: {
  token: string; organizations: OrganizationOption[] | null;
}) {
  const [organizationId, setOrganizationId] = useState(organizations?.[0]?.id ?? "");
  const [items, setItems] = useState<HodogramSummary[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [detail, setDetail] = useState<HodogramDetail | null>(null);
  const [draft, setDraft] = useState<MatrixDraft>({});
  const [newName, setNewName] = useState("");
  const [newDescription, setNewDescription] = useState("");
  const [header, setHeader] = useState({ name: "", description: "", isActive: true });
  const [phaseName, setPhaseName] = useState("");
  const [stepNames, setStepNames] = useState<Record<string, string>>({});
  const [phaseEdit, setPhaseEdit] = useState<{ id: string; name: string; sortOrder: string } | null>(null);
  const [stepEdit, setStepEdit] = useState<StepEdit | null>(null);
  const [serverIssues, setServerIssues] = useState<Record<string, string[]>>({});
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const root = `/api/organizations/${organizationId}/hodograms`;

  const show = useCallback((next: HodogramDetail) => {
    setDetail(next); setDraft(draftFromDetail(next)); setServerIssues({});
    setHeader({ name: next.name, description: next.description ?? "", isActive: next.isActive });
  }, []);

  const loadList = useCallback(async () => {
    if (!organizationId) { setItems([]); return; }
    try { setItems(await callApi<HodogramSummary[]>(root, {}, token)); }
    catch (problem) { setError(errorText(problem)); }
  }, [organizationId, root, token]);

  const loadDetail = useCallback(async (id: string) => {
    if (!organizationId || !id) { setDetail(null); return; }
    try { show(await callApi<HodogramDetail>(`${root}/${id}`, {}, token)); }
    catch (problem) { setError(errorText(problem)); }
  }, [organizationId, root, show, token]);

  useEffect(() => { setSelectedId(""); setDetail(null); void loadList(); }, [loadList]);
  useEffect(() => { void loadDetail(selectedId); }, [loadDetail, selectedId]);

  async function run(action: () => Promise<HodogramDetail | void>, success: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const result = await action();
      if (result) show(result);
      setNotice(success);
      await loadList();
    } catch (problem) {
      if (problem instanceof ApiError && problem.status === 409 && detail) {
        setError(`${errorText(problem)} Prikazano je najnovije stanje.`);
        await loadDetail(detail.id);
      } else {
        if (problem instanceof ApiError && problem.status === 422)
          setServerIssues(issuesByStep(problem.issues as unknown as RaciIssue[]));
        setError(errorText(problem));
      }
    } finally { pending.current = false; setBusy(false); }
  }

  const send = (path: string, method: string, body?: object) =>
    callApi<HodogramDetail>(path, { method, ...(body ? { body: JSON.stringify(body) } : {}) }, token);

  function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void run(async () => {
      const created = await send(root, "POST", { name: newName, description: newDescription || null });
      setNewName(""); setNewDescription(""); setSelectedId(created.id);
      return created;
    }, "Hodogram je kreiran.");
  }

  function saveHeader(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;
    void run(() => send(`${root}/${detail.id}`, "PUT", { ...header,
      description: header.description || null, revision: detail.revision }), "Hodogram je sačuvan.");
  }

  function addPhase(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail) return;
    void run(async () => {
      const result = await send(`${root}/${detail.id}/phases`, "POST", { name: phaseName, revision: detail.revision });
      setPhaseName("");
      return result;
    }, "Faza je dodana.");
  }

  function addStep(event: FormEvent<HTMLFormElement>, phaseId: string) {
    event.preventDefault();
    if (!detail) return;
    void run(async () => {
      const result = await send(`${root}/${detail.id}/phases/${phaseId}/steps`, "POST",
        { name: stepNames[phaseId] ?? "", revision: detail.revision });
      setStepNames((current) => ({ ...current, [phaseId]: "" }));
      return result;
    }, "Korak je dodan.");
  }

  function savePhase(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail || !phaseEdit) return;
    void run(async () => {
      const result = await send(`${root}/${detail.id}/phases/${phaseEdit.id}`, "PUT",
        { name: phaseEdit.name, sortOrder: Number(phaseEdit.sortOrder), revision: detail.revision });
      setPhaseEdit(null);
      return result;
    }, "Faza je sačuvana.");
  }

  function deletePhase(phaseId: string, name: string) {
    if (!detail || !window.confirm(`Ukloniti fazu „${name}“?`)) return;
    void run(() => send(`${root}/${detail.id}/phases/${phaseId}?revision=${detail.revision}`, "DELETE"),
      "Faza je uklonjena.");
  }

  function saveStep(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!detail || !stepEdit) return;
    void run(async () => {
      const result = await send(`${root}/${detail.id}/steps/${stepEdit.id}`, "PUT", { name: stepEdit.name,
        description: stepEdit.description || null, phaseId: stepEdit.phaseId,
        sortOrder: Number(stepEdit.sortOrder), revision: detail.revision });
      setStepEdit(null);
      return result;
    }, "Korak je sačuvan.");
  }

  function deleteStep(step: HodogramStep) {
    if (!detail || !window.confirm(`Ukloniti korak „${step.name}“ i njegove RACI dodjele?`)) return;
    void run(() => send(`${root}/${detail.id}/steps/${step.id}?revision=${detail.revision}`, "DELETE"),
      "Korak je uklonjen.");
  }

  function saveMatrix() {
    if (!detail) return;
    void run(() => send(`${root}/${detail.id}/matrix`, "PUT", matrixPayload(detail, draft)),
      "RACI matrica je sačuvana.");
  }

  function setCell(stepId: string, role: string, value: RaciValue | "") {
    setDraft((current) => ({ ...current, [stepId]: { ...current[stepId], [role]: value } }));
    setServerIssues((current) => { const rest = { ...current }; delete rest[stepId]; return rest; });
  }

  const issues = detail ? { ...draftIssues(detail, draft), ...serverIssues } : {};
  const changed = detail ? isDraftChanged(detail, draft) : false;
  // Building representatives may view their organization's processes but not change them.
  const readOnly = organizations?.find((item) => item.id === organizationId)?.readOnly ?? false;
  const extraColumns = readOnly ? 1 : 2;
  const stepCount = detail?.phases.reduce((total, phase) => total + phase.steps.length, 0) ?? 0;

  return <div className="hodogram-page">
    <SectionTitle title="Hodogram" description="Faze i koraci procesa upravljanja s RACI matricom po ulogama u sistemu." />
    <div className="finance-selector">
      {organizations === null
        ? <Lookup token={token} label="Organizacija" path="/api/system/organizations" value={organizationId}
          onChange={setOrganizationId} />
        : <label>Organizacija<select value={organizationId} onChange={(event) => setOrganizationId(event.target.value)}>
          {organizations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
    </div>
    {error && <p className="error" role="alert">{error}</p>}
    {notice && <p className="notice">{notice}</p>}
    {organizationId && <div className="admin-layout">
      <section className="panel">
        <h2>Hodogrami</h2>
        <div className="hodogram-list">{items.map((item) => <button type="button" key={item.id}
          className={item.id === selectedId ? "active" : ""} onClick={() => setSelectedId(item.id)}>
          <strong>{item.name}</strong><span>{item.phaseCount} faza · {item.stepCount} koraka</span>
          <Status active={item.isActive} /></button>)}
          {items.length === 0 && <p className="empty-inline">Nema hodograma.</p>}</div>
        {!readOnly && <form className="admin-form" onSubmit={create}>
          <h3>Novi hodogram</h3>
          <label>Naziv<input required maxLength={200} value={newName} onChange={(event) => setNewName(event.target.value)} /></label>
          <label>Opis<input maxLength={2000} value={newDescription} onChange={(event) => setNewDescription(event.target.value)} /></label>
          <button type="submit" disabled={busy}>Kreiraj</button>
        </form>}
      </section>
      {detail && <section className="panel hodogram-detail">
        {readOnly ? <div className="hodogram-header"><h2>{detail.name}</h2>{detail.description && <p>{detail.description}</p>}
          <span className="notice">Pregled (samo čitanje)</span></div>
        : <form className="hodogram-header" onSubmit={saveHeader}>
          <label>Naziv<input required maxLength={200} value={header.name}
            onChange={(event) => setHeader({ ...header, name: event.target.value })} /></label>
          <label>Opis<input maxLength={2000} value={header.description}
            onChange={(event) => setHeader({ ...header, description: event.target.value })} /></label>
          <label className="checkbox"><input type="checkbox" checked={header.isActive}
            onChange={(event) => setHeader({ ...header, isActive: event.target.checked })} />Aktivan</label>
          <button type="submit" disabled={busy}>Sačuvaj</button>
        </form>}
        <p className={detail.isValid ? "notice" : "field-error"}>
          {detail.isValid ? "Sačuvana matrica je ispravna." : `Sačuvana matrica ima ${detail.issues.length} problema.`}
          {" "}Pravilo: svaki korak ima najmanje jedan R i tačno jedan A; C i I su opcionalni.</p>
        <p className="hodogram-legend">{raciValues.map((value) => <span key={value}>{raciLabels[value]}</span>)}</p>
        <div className="table-scroll hodogram-matrix"><table>
          <thead><tr><th>Faza / korak</th>{detail.roles.map((role) => <th key={role}>{roleLabel(role)}</th>)}
            {!readOnly && <th>Akcije</th>}</tr></thead>
          <tbody>{detail.phases.map((phase) => <Fragment key={phase.id}>
            <tr className="phase-row"><td colSpan={detail.roles.length + extraColumns}>
              {phaseEdit?.id === phase.id
                ? <form className="inline-form" onSubmit={savePhase}>
                  <input aria-label="Naziv faze" required maxLength={200} value={phaseEdit.name}
                    onChange={(event) => setPhaseEdit({ ...phaseEdit, name: event.target.value })} />
                  <input aria-label="Redoslijed faze" type="number" min={0} max={10000} value={phaseEdit.sortOrder}
                    onChange={(event) => setPhaseEdit({ ...phaseEdit, sortOrder: event.target.value })} />
                  <button type="submit" disabled={busy}>Sačuvaj</button>
                  <button type="button" onClick={() => setPhaseEdit(null)}>Odustani</button></form>
                : <div className="phase-title"><strong>{phase.name}</strong>{!readOnly && <span className="actions">
                  <button type="button" onClick={() => setPhaseEdit({ id: phase.id, name: phase.name,
                    sortOrder: String(phase.sortOrder) })}>Uredi</button>
                  <button type="button" disabled={busy} onClick={() => deletePhase(phase.id, phase.name)}>Ukloni</button>
                </span>}</div>}
            </td></tr>
            {phase.steps.map((step) => <Fragment key={step.id}><tr className={issues[step.id] ? "invalid-row" : ""}>
              <th scope="row">{step.name}{step.description && <small>{step.description}</small>}
                {issues[step.id] && <small className="row-issues" role="status">{issues[step.id].join(" ")}</small>}</th>
              {detail.roles.map((role) => <td key={role}>
                <select aria-label={`${step.name} – ${roleLabel(role)}`} className={`raci-cell raci-${draft[step.id]?.[role] || "none"}`}
                  value={draft[step.id]?.[role] ?? ""} disabled={busy || readOnly}
                  onChange={(event) => setCell(step.id, role, event.target.value as RaciValue | "")}>
                  <option value="">–</option>{raciValues.map((value) => <option key={value} value={value}>{value}</option>)}
                </select></td>)}
              {!readOnly && <td><span className="actions">
                <button type="button" onClick={() => setStepEdit({ id: step.id, name: step.name,
                  description: step.description ?? "", phaseId: phase.id, sortOrder: String(step.sortOrder) })}>Uredi</button>
                <button type="button" disabled={busy} onClick={() => deleteStep(step)}>Ukloni</button></span></td>}
            </tr>
            {stepEdit?.id === step.id && <tr><td colSpan={detail.roles.length + 2}>
              <form className="inline-form" onSubmit={saveStep}>
                <input aria-label="Naziv koraka" required maxLength={300} value={stepEdit.name}
                  onChange={(event) => setStepEdit({ ...stepEdit, name: event.target.value })} />
                <input aria-label="Opis koraka" maxLength={2000} placeholder="Opis" value={stepEdit.description}
                  onChange={(event) => setStepEdit({ ...stepEdit, description: event.target.value })} />
                <select aria-label="Faza koraka" value={stepEdit.phaseId}
                  onChange={(event) => setStepEdit({ ...stepEdit, phaseId: event.target.value })}>
                  {detail.phases.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
                <input aria-label="Redoslijed koraka" type="number" min={0} max={10000} value={stepEdit.sortOrder}
                  onChange={(event) => setStepEdit({ ...stepEdit, sortOrder: event.target.value })} />
                <button type="submit" disabled={busy}>Sačuvaj</button>
                <button type="button" onClick={() => setStepEdit(null)}>Odustani</button></form></td></tr>}
            </Fragment>)}
            {!readOnly && <tr><td colSpan={detail.roles.length + 2}>
              <form className="inline-form" onSubmit={(event) => addStep(event, phase.id)}>
                <input aria-label={`Novi korak u fazi ${phase.name}`} required maxLength={300} placeholder="Novi korak / operacija"
                  value={stepNames[phase.id] ?? ""}
                  onChange={(event) => setStepNames((current) => ({ ...current, [phase.id]: event.target.value }))} />
                <button type="submit" disabled={busy}>Dodaj korak</button></form></td></tr>}
          </Fragment>)}</tbody>
        </table></div>
        {detail.phases.length === 0 && <p className="empty-inline">{readOnly ? "Hodogram još nema faza." : "Dodajte prvu fazu procesa."}</p>}
        {!readOnly && <><form className="inline-form" onSubmit={addPhase}>
          <input aria-label="Nova faza" required maxLength={200} placeholder="Nova faza" value={phaseName}
            onChange={(event) => setPhaseName(event.target.value)} />
          <button type="submit" disabled={busy}>Dodaj fazu</button></form>
        <div className="form-actions">
          <button type="button" disabled={busy || !changed || stepCount === 0}
            onClick={saveMatrix}>Sačuvaj RACI matricu</button>
          <button type="button" className="secondary" disabled={busy || !changed}
            onClick={() => setDraft(draftFromDetail(detail))}>Poništi izmjene</button>
        </div></>}
      </section>}
    </div>}
  </div>;
}
