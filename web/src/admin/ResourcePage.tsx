import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { ApiError, callApi, errorText, Page } from "../api";
import { resourceKey } from "../dataSync.ts";
import { useChangeRevision } from "../useChangeRevision.ts";
import { updateDependentForm } from "../formDependencies.ts";
import { Lookup, Pager, SectionTitle, Status } from "./shared";
import { generateStrongPassword } from "../passwords.ts";
import type { LookupTypeCode } from "../lookupOptions.ts";
import LookupSelect, { LookupLabel } from "./LookupSelect";
import { emptyImageDraft, ImageDraft, ImageUploadField } from "./ZevCover";
import { readFileBase64 } from "../zevImage.ts";

export type ResourceRow = { id: string; rowVersion?: string; isActive?: boolean; [key: string]: unknown };
export type ResourceField = {
  key: string;
  label: string;
  required?: boolean;
  kind?: "text" | "email" | "password" | "number" | "date" | "select" | "lookup";
  options?: string[];
  // Display names of the options (the stored value stays the option itself).
  optionLabels?: Record<string, string>;
  lookupPath?: (form: Record<string, string>) => string;
  // Options from GET /api/lookups/{lookupType}; the stored value is the item code.
  lookupType?: LookupTypeCode;
  dependsOn?: string;
  formOnly?: boolean;
  createOnly?: boolean;
  maxLength?: number;
  step?: string;
};
export type ResourceConfig = {
  title: string;
  description: string;
  path: string;
  fields: ResourceField[];
  columns: { key: string; label: string; lookupType?: LookupTypeCode; labels?: Record<string, string> }[];
  allowEdit?: boolean;
  allowCreate?: boolean;
  allowStatus?: boolean;
  // "Obriši": DELETE {path}/{id}; the record moves to Administration → "Obrisani redovi".
  allowDelete?: boolean;
  passwordReset?: boolean;
  search?: boolean;
  filters?: ResourceField[];
  // Photo of the record (ZEV cover): sent to {path(id)} after the record itself is saved.
  image?: { label: string; path: (id: string) => string; urlKey: string; nameKey: string };
};

function initialForm(fields: ResourceField[]): Record<string, string> {
  return Object.fromEntries(fields.map((field) => [field.key, ""]));
}

export default function ResourcePage({ config, token, onChanged, onCreated }: {
  config: ResourceConfig; token: string; onChanged: () => void; onCreated?: (id: string) => void;
}) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState<Record<string, string>>({});
  const [data, setData] = useState<Page<ResourceRow>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [form, setForm] = useState<Record<string, string>>(() => initialForm(config.fields));
  const [editingId, setEditingId] = useState("");
  const [rowVersion, setRowVersion] = useState("");
  const [error, setError] = useState("");
  const [loadError, setLoadError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [resetTarget, setResetTarget] = useState<ResourceRow | null>(null);
  const [imageDraft, setImageDraft] = useState<ImageDraft>(emptyImageDraft);
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [resetPassword, setResetPassword] = useState("");
  const [resetConfirm, setResetConfirm] = useState("");
  const [requireChange, setRequireChange] = useState(true);
  const [showResetPassword, setShowResetPassword] = useState(false);
  const [showCreatePassword, setShowCreatePassword] = useState(false);
  const [resetError, setResetError] = useState("");
  const [loading, setLoading] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(resourceKey(config.path) ?? "");

  const load = useCallback(async () => {
    const currentRequest = ++requestId.current;
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: "20" });
      if (config.search && search) params.set("search", search);
      for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
      const result = await callApi<Page<ResourceRow>>(`${config.path}?${params}`, {}, token);
      if (currentRequest !== requestId.current) return;
      if (page > 1 && result.items.length === 0) { setPage(Math.max(1, Math.ceil(result.totalCount / 20))); return; }
      setData(result); setLoadError("");
    } catch (problem) { if (currentRequest === requestId.current) setLoadError(errorText(problem)); }
    finally { if (currentRequest === requestId.current) setLoading(false); }
  }, [config.path, config.search, page, search, filters, token]);

  useEffect(() => { void load(); return () => { requestId.current++; }; }, [load, revision]);

  function updateForm(key: string, value: string) {
    setForm((current) => updateDependentForm(current, key, value, config.fields));
  }

  function edit(item: ResourceRow) {
    setForm(Object.fromEntries(config.fields.map((field) => [field.key, String(item[field.key] ?? "")])));
    setEditingId(item.id);
    setRowVersion(item.rowVersion ?? "");
    setImageDraft(emptyImageDraft);
    setImageUrl(config.image ? (item[config.image.urlKey] as string | null) ?? null : null);
    setError("");
  }

  function reset() {
    setForm(initialForm(config.fields));
    setShowCreatePassword(false);
    setEditingId("");
    setRowVersion("");
    setImageDraft(emptyImageDraft);
    setImageUrl(null);
  }

  // The record is saved first (a new ZEV gets its id), then the photo is uploaded or removed.
  async function saveImage(id: string): Promise<string | null> {
    if (!config.image || (!imageDraft.file && !imageDraft.remove)) return null;
    try {
      if (imageDraft.file) await callApi(config.image.path(id), { method: "POST", body: JSON.stringify({
        fileName: imageDraft.file.name, contentType: imageDraft.file.type, contentBase64: await readFileBase64(imageDraft.file) }) }, token);
      else await callApi(config.image.path(id), { method: "DELETE" }, token);
      return null;
    } catch (problem) { return errorText(problem); }
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current) return;
    pending.current = true;
    setBusy(true); setError(""); setNotice("");
    const payload: Record<string, unknown> = {};
    for (const field of config.fields) {
      if (field.formOnly || (editingId && field.createOnly)) continue;
      const value = form[field.key]?.trim() ?? "";
      payload[field.key] = field.kind === "number" ? Number(value) : value || null;
    }
    if (editingId) payload.rowVersion = rowVersion;
    try {
      const saved = await callApi<ResourceRow>(editingId ? `${config.path}/${editingId}` : config.path, {
        method: editingId ? "PUT" : "POST", body: JSON.stringify(payload),
      }, token);
      if (!editingId && saved?.id) onCreated?.(saved.id);
      const imageError = await saveImage(editingId || saved?.id || "");
      reset(); setNotice("Zapis je sačuvan.");
      if (imageError) setError(`Zapis je sačuvan, ali slika nije: ${imageError}`);
      onChanged();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function remove(item: ResourceRow) {
    if (pending.current || !window.confirm("Obrisati ovaj zapis? Sistemski administrator ga može vratiti iz „Obrisani redovi“.")) return;
    pending.current = true; setBusy(true);
    setError(""); setNotice("");
    try {
      await callApi(`${config.path}/${item.id}`, { method: "DELETE" }, token);
      if (editingId === item.id) reset();
      setNotice("Zapis je obrisan.");
      onChanged();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function changeStatus(item: ResourceRow) {
    if (pending.current) return;
    if (item.isActive && !window.confirm("Deaktivirati ovaj zapis?")) return;
    pending.current = true; setBusy(true);
    setError(""); setNotice("");
    try {
      await callApi(`${config.path}/${item.id}/status`, {
        method: "PATCH", body: JSON.stringify({ isActive: !item.isActive, rowVersion: item.rowVersion }),
      }, token);
      setNotice("Status je promijenjen.");
      onChanged();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function closeReset() {
    setResetTarget(null); setResetPassword(""); setResetConfirm("");
    setShowResetPassword(false); setResetError(""); setRequireChange(true);
  }

  function openReset(item: ResourceRow) {
    closeReset();
    setResetTarget(item);
  }

  async function submitReset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!resetTarget || busy || pending.current) return;
    if (resetPassword !== resetConfirm) {
      setResetError("Lozinke se ne podudaraju.");
      return;
    }
    pending.current = true; setBusy(true); setResetError("");
    try {
      await callApi(`${config.path}/${resetTarget.id}/reset-password`, {
        method: "POST", body: JSON.stringify({ newPassword: resetPassword,
          confirmPassword: resetConfirm, requireChange })
      }, token);
      closeReset();
      setNotice("Lozinka je uspješno resetovana.");
      onChanged();
    } catch (problem) { setResetError(problem instanceof ApiError && problem.status === 403
      ? "Nemate pravo resetovati lozinku ovog korisnika." : errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  return <>
    <SectionTitle title={config.title} description={config.description} />
    <div className="admin-layout">
      {(config.allowCreate !== false || editingId) && <section className="panel"><h2>{editingId ? "Uredi zapis" : "Novi zapis"}</h2>
        <form className="admin-form" onSubmit={save}>
          {config.fields.filter((field) => !editingId || !field.createOnly).map((field) =>
            field.kind === "lookup" ? <Lookup key={field.key} token={token} label={field.label} required={field.required}
              path={field.lookupPath?.(form) ?? ""} disabled={!field.lookupPath?.(form)} value={form[field.key] ?? ""}
              onChange={(value) => updateForm(field.key, value)} />
              : <label key={field.key}>{field.label}
                {field.lookupType ? <LookupSelect typeCode={field.lookupType} token={token} required={field.required}
                  value={form[field.key] ?? ""} onChange={(value) => updateForm(field.key, value)} />
                : field.kind === "select" ? <select required={field.required} value={form[field.key] ?? ""}
                  onChange={(event) => updateForm(field.key, event.target.value)}>
                  <option value="">Odaberite</option>{field.options?.map((option) => <option key={option} value={option}>{field.optionLabels?.[option] ?? option}</option>)}
                </select> : <input type={field.kind === "password" && showCreatePassword ? "text" : field.kind ?? "text"}
                  required={field.required}
                  maxLength={field.maxLength} step={field.step} value={form[field.key] ?? ""}
                  onChange={(event) => updateForm(field.key, event.target.value)} />}
              </label>)}
          {config.image && <ImageUploadField label={config.image.label} token={token} currentUrl={imageUrl}
            name={form[config.image.nameKey] ?? ""} draft={imageDraft} onChange={setImageDraft} />}
          {config.passwordReset && !editingId && <div className="form-actions">
            <button type="button" className="secondary" onClick={() => {
              const generated = generateStrongPassword();
              setForm((current) => ({ ...current, password: generated }));
              setShowCreatePassword(true);
            }}>Generiši jaku lozinku</button>
            <button type="button" className="secondary" disabled={!form.password} onClick={() => {
              if (!navigator.clipboard) { setError("Kopiranje nije dostupno u ovom pregledniku."); return; }
              void navigator.clipboard.writeText(form.password)
                .catch(() => setError("Kopiranje nije uspjelo. Provjerite dozvole preglednika."));
            }}>Kopiraj</button>
          </div>}
          <div className="form-actions"><button type="submit" disabled={busy}>{busy ? "Čuvanje…" : editingId ? "Ažuriraj" : "Sačuvaj"}</button>
            {editingId && <button className="secondary" type="button" onClick={reset}>Otkaži</button>}</div>
        </form>
      </section>}
      <section className="panel table-panel"><div className="panel-header"><h2>Pregled zapisa</h2>
        {config.search && <input aria-label="Pretraga" placeholder="Pretraži…" value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1); }} />}</div>
        {config.filters && <div className="filter-row">{config.filters.map((field) =>
          field.kind === "lookup" ? <Lookup key={field.key} token={token} label={field.label}
            path={field.lookupPath?.(filters) ?? ""} value={filters[field.key] ?? ""}
            onChange={(value) => { setFilters((current) => ({ ...current, [field.key]: value })); setPage(1); }} />
            : <label key={field.key}>{field.label}<select value={filters[field.key] ?? ""}
              onChange={(event) => { setFilters((current) => ({ ...current, [field.key]: event.target.value })); setPage(1); }}>
              <option value="">Svi</option>{field.options?.map((option) => <option key={option} value={option}>{field.optionLabels?.[option] ?? option}</option>)}
            </select></label>)}</div>}
        {error && <p className="error" role="alert">{error}</p>}
        {loadError && <p className="error" role="alert">Učitavanje liste: {loadError}</p>}
        {notice && <p className="notice" role="status">{notice}</p>}
        {loading ? <p className="empty-inline">Učitavanje…</p> : <>
          <div className="table-scroll"><table><thead><tr>{config.columns.map((column) => <th key={column.key}>{column.label}</th>)}
            <th>Status</th><th>Radnje</th></tr></thead><tbody>{data.items.map((item) => <tr key={item.id}>
              {config.columns.map((column) => <td key={column.key}>{column.lookupType
                ? <LookupLabel typeCode={column.lookupType} token={token} value={item[column.key] as string | null} />
                : column.labels ? column.labels[String(item[column.key])] ?? String(item[column.key] ?? "—")
                : String(item[column.key] ?? "—")}</td>)}
              <td>{item.isActive === undefined ? "—" : <Status active={item.isActive} />}</td>
              <td className="actions">{config.allowEdit && <button type="button" disabled={busy} onClick={() => edit(item)}>Uredi</button>}
                {config.passwordReset && <button type="button" disabled={busy}
                  onClick={() => openReset(item)}>Resetuj lozinku</button>}
                {config.allowStatus && <button type="button" disabled={busy} onClick={() => changeStatus(item)}>
                  {item.isActive ? "Deaktiviraj" : "Aktiviraj"}</button>}
                {config.allowDelete && <button type="button" className="danger" disabled={busy}
                  onClick={() => void remove(item)}>Obriši</button>}</td></tr>)}</tbody></table></div>
          {data.items.length === 0 && <p className="empty-inline">Nema zapisa za prikaz.</p>}
          <Pager {...data} onPage={setPage} />
        </>}
      </section>
    </div>
    {resetTarget && <div className="modal-backdrop"><section className="modal-card" role="dialog"
      aria-modal="true" aria-labelledby="reset-password-heading">
      <h2 id="reset-password-heading">Resetuj lozinku</h2>
      <p>Korisnik: {String(resetTarget.email ?? "")}</p>
      <form className="admin-form" onSubmit={submitReset}>
        <label>Nova privremena lozinka <input type={showResetPassword ? "text" : "password"}
          autoComplete="new-password" required value={resetPassword}
          onChange={(event) => setResetPassword(event.target.value)} /></label>
        <label>Potvrdi lozinku <input type={showResetPassword ? "text" : "password"}
          autoComplete="new-password" required value={resetConfirm}
          onChange={(event) => setResetConfirm(event.target.value)} /></label>
        <div className="form-actions"><button type="button" className="secondary" onClick={() => {
          const generated = generateStrongPassword();
          setResetPassword(generated); setResetConfirm(generated); setShowResetPassword(true);
        }}>Generiši jaku lozinku</button>
          <button type="button" className="secondary" disabled={!resetPassword}
            onClick={() => {
              if (!navigator.clipboard) { setResetError("Kopiranje nije dostupno u ovom pregledniku."); return; }
              void navigator.clipboard.writeText(resetPassword)
                .catch(() => setResetError("Kopiranje nije uspjelo. Provjerite dozvole preglednika."));
            }}>Kopiraj</button></div>
        <label className="checkbox-label"><input type="checkbox" checked={requireChange}
          onChange={(event) => setRequireChange(event.target.checked)} />
          Zahtijevaj promjenu lozinke pri sljedećoj prijavi</label>
        {resetError && <p className="error" role="alert">{resetError}</p>}
        <div className="form-actions"><button type="button" className="secondary" disabled={busy}
          onClick={closeReset}>Otkaži</button>
          <button type="submit" disabled={busy}>{busy ? "Resetovanje…" : "Resetuj lozinku"}</button></div>
      </form>
    </section></div>}
  </>;
}
