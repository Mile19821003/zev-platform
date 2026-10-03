import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Organization, Page } from "../api";
import { useChangeRevision } from "../useChangeRevision.ts";
import { Pager, SectionTitle, Status } from "./shared";

type OrganizationForm = { id: string; code: string; name: string; rowVersion: string;
  addressLine: string; cityMunicipality: string; taxIdentifier: string; contactName: string;
  email: string; phone: string };
const blankForm: OrganizationForm = { id: "", code: "", name: "", rowVersion: "",
  addressLine: "", cityMunicipality: "", taxIdentifier: "", contactName: "", email: "", phone: "" };

export default function OrganizationsPage({ token, onChanged }: { token: string; onChanged: () => void }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [data, setData] = useState<Page<Organization>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [form, setForm] = useState<OrganizationForm>(blankForm);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision("organizations");

  const load = useCallback(async () => {
    const currentRequest = ++requestId.current;
    setLoading(true);
    try {
      const result = await callApi<Page<Organization>>(`/api/system/organizations?page=${page}&pageSize=20&search=${encodeURIComponent(search)}`, {}, token);
      if (currentRequest !== requestId.current) return;
      if (page > 1 && result.items.length === 0) { setPage(Math.max(1, Math.ceil(result.totalCount / 20))); return; }
      setData(result); setLoadError("");
    } catch (problem) { if (currentRequest === requestId.current) setLoadError(errorText(problem)); }
    finally { if (currentRequest === requestId.current) setLoading(false); }
  }, [page, search, token]);

  useEffect(() => { void load(); return () => { requestId.current++; }; }, [load, revision]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current) return;
    pending.current = true;
    setBusy(true); setError(""); setNotice("");
    try {
      const path = form.id ? `/api/system/organizations/${form.id}` : "/api/system/organizations";
      await callApi(path, {
        method: form.id ? "PUT" : "POST",
        body: JSON.stringify({ ...form, id: undefined, ...(form.id ? { rowVersion: form.rowVersion } : { rowVersion: undefined }) }),
      }, token);
      setForm(blankForm);
      setNotice("Organizacija je sačuvana.");
      onChanged();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function changeStatus(item: Organization) {
    if (pending.current) return;
    if (item.isActive && !window.confirm(`Deaktivirati organizaciju „${item.name}“?`)) return;
    pending.current = true; setBusy(true);
    setError(""); setNotice("");
    try {
      await callApi(`/api/system/organizations/${item.id}/status`, {
        method: "PATCH", body: JSON.stringify({ isActive: !item.isActive, rowVersion: item.rowVersion }),
      }, token);
      setNotice("Status je promijenjen."); onChanged();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  return <>
    <SectionTitle title="Organizacije / upravnici" description="Pregled i upravljanje organizacijama koje vode ZEV-ove." />
    <div className="admin-layout">
      <section className="panel"><h2>{form.id ? "Uredi organizaciju" : "Nova organizacija"}</h2>
        <form className="admin-form" onSubmit={save}>
          <label>Šifra<input required maxLength={50} value={form.code} onChange={(event) => setForm({ ...form, code: event.target.value })} /></label>
          <label>Naziv<input required maxLength={200} value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></label>
          <label>Adresa<input maxLength={250} value={form.addressLine} onChange={(event) => setForm({ ...form, addressLine: event.target.value })} /></label>
          <label>Grad / opština<input maxLength={120} value={form.cityMunicipality} onChange={(event) => setForm({ ...form, cityMunicipality: event.target.value })} /></label>
          <label>JIB / PIB<input maxLength={32} value={form.taxIdentifier} onChange={(event) => setForm({ ...form, taxIdentifier: event.target.value })} /></label>
          <label>Kontakt osoba<input maxLength={200} value={form.contactName} onChange={(event) => setForm({ ...form, contactName: event.target.value })} /></label>
          <label>Email<input type="email" maxLength={320} value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} /></label>
          <label>Telefon<input maxLength={40} value={form.phone} onChange={(event) => setForm({ ...form, phone: event.target.value })} /></label>
          <div className="form-actions"><button type="submit" disabled={busy}>{busy ? "Čuvanje…" : "Sačuvaj"}</button>
            {form.id && <button type="button" className="secondary" onClick={() => setForm(blankForm)}>Otkaži</button>}</div>
        </form>
      </section>
      <section className="panel table-panel"><div className="panel-header"><h2>Sve organizacije</h2>
        <input aria-label="Pretraži organizacije" placeholder="Pretraži naziv ili šifru" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></div>
        {error && <p className="error" role="alert">{error}</p>}{loadError && <p className="error" role="alert">Učitavanje liste: {loadError}</p>}{notice && <p className="notice" role="status">{notice}</p>}
        {loading ? <p className="empty-inline">Učitavanje…</p> : <div className="table-scroll"><table><thead><tr><th>Šifra</th><th>Naziv</th><th>Status</th><th>Radnje</th></tr></thead>
          <tbody>{data.items.map((item) => <tr key={item.id}><td>{item.code}</td><td>{item.name}</td><td><Status active={item.isActive} /></td>
            <td className="actions"><button type="button" disabled={busy} onClick={() => setForm({ id: item.id, code: item.code, name: item.name, rowVersion: item.rowVersion,
              addressLine: item.addressLine ?? "", cityMunicipality: item.cityMunicipality ?? "", taxIdentifier: item.taxIdentifier ?? "",
              contactName: item.contactName ?? "", email: item.email ?? "", phone: item.phone ?? "" })}>Uredi</button>
              <button type="button" disabled={busy} onClick={() => changeStatus(item)}>{item.isActive ? "Deaktiviraj" : "Aktiviraj"}</button></td></tr>)}</tbody></table></div>}
        {!loading && data.items.length === 0 && <p className="empty-inline">Nema organizacija za prikaz.</p>}
        {!loading && <Pager {...data} onPage={setPage} />}
      </section>
    </div>
  </>;
}
