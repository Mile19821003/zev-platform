import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";

type ZevOption = { id: string; code: string; name: string };
type Unit = { id: string; zevId: string; code: string };
type Ticket = { id: string; propertyUnitId: string | null; category: string; description: string;
  location: string; priority: string; status: string; rowVersion: string };
type Order = { id: string; title: string; description: string; status: string; priority: string;
  ticketId: string | null; costAmount: number | null; dueOn: string | null; rowVersion: string };
type Plan = { id: string; title: string; category: string; frequencyDays: number; firstDueOn: string };
type Schedule = { id: string; title: string; dueOn: string; status: string };

export default function MaintenancePage({ token, zevs, role, permissions }: {
  token: string; zevs: ZevOption[]; role: "manager" | "representative" | "owner"; permissions?: string[];
}) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [units, setUnits] = useState<Unit[]>([]);
  const [unitId, setUnitId] = useState("");
  const [tickets, setTickets] = useState<Page<Ticket>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [orders, setOrders] = useState<Page<Order>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [plans, setPlans] = useState<Plan[]>([]);
  const [calendar, setCalendar] = useState<Schedule[]>([]);
  const [ticketPage, setTicketPage] = useState(1);
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [location, setLocation] = useState("");
  const [priority, setPriority] = useState("Normal");
  const [photo, setPhoto] = useState<File | null>(null);
  const [orderTicketId, setOrderTicketId] = useState("");
  const [orderTitle, setOrderTitle] = useState("");
  const [orderDescription, setOrderDescription] = useState("");
  const [orderCost, setOrderCost] = useState("");
  const [orderDueOn, setOrderDueOn] = useState("");
  const [planTitle, setPlanTitle] = useState("");
  const [planCategory, setPlanCategory] = useState("");
  const [frequencyDays, setFrequencyDays] = useState("");
  const [firstDueOn, setFirstDueOn] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:maintenance`);

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const prefix = `/api/zevs/${zevId}/maintenance`;
      const ticketData = await callApi<Page<Ticket>>(`${prefix}/tickets?page=${ticketPage}`, {}, token);
      if (currentRequest === requestId.current && ticketPage > 1 && ticketData.items.length === 0) {
        setTicketPage(Math.max(1, Math.ceil(ticketData.totalCount / 20))); return;
      }
      if (role === "owner") {
        const owned = await callApi<Unit[]>("/api/me/property-units", {}, token);
        if (currentRequest === requestId.current) {
          setTickets(ticketData); setUnits(owned.filter((item) => item.zevId === zevId));
        }
      } else {
        const [orderData, planData, calendarData] = await Promise.all([
          callApi<Page<Order>>(`${prefix}/work-orders`, {}, token),
          callApi<Plan[]>(`${prefix}/plans`, {}, token),
          callApi<Schedule[]>(`${prefix}/calendar`, {}, token),
        ]);
        if (currentRequest === requestId.current) {
          setTickets(ticketData); setOrders(orderData); setPlans(planData); setCalendar(calendarData);
        }
      }
      if (currentRequest === requestId.current) setError("");
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, ticketPage, role, token]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function submit(path: string, body: object, message: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/maintenance/${path}`, { method: "POST", body: JSON.stringify(body) }, token);
      setNotice(message);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function createTicket(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return; pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const fileIds: string[] = [];
      if (photo) {
        if (photo.size > 5_000_000 || !["image/jpeg", "image/png"].includes(photo.type)) {
          setError("Fotografija mora biti JPG ili PNG do 5 MB."); return;
        }
        const contentBase64 = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(photo);
        });
        const uploaded = await callApi<{ id: string }>(`/api/zevs/${zevId}/files`, { method: "POST",
          body: JSON.stringify({ fileName: photo.name, contentType: photo.type, contentBase64 }) }, token);
        fileIds.push(uploaded.id);
      }
      await callApi(`/api/zevs/${zevId}/maintenance/tickets`, { method: "POST", body: JSON.stringify({
        propertyUnitId: unitId || null, category, description, location, priority, fileIds,
      }) }, token);
      setNotice("Kvar je prijavljen."); setDescription(""); setPhoto(null);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function changeStatus(kind: "tickets" | "work-orders", item: Ticket | Order, status: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError("");
    try {
      await callApi(`/api/zevs/${zevId}/maintenance/${kind}/${item.id}/status`, { method: "PATCH",
        body: JSON.stringify({ status, rowVersion: item.rowVersion }) }, token);
      setNotice("Status je promijenjen.");
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function createOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("work-orders", { ticketId: orderTicketId || null, supplierId: null,
      title: orderTitle, description: orderDescription, priority, costAmount: orderCost ? Number(orderCost) : null,
      dueOn: orderDueOn || null }, "Radni nalog je kreiran.");
  }

  function createPlan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("plans", { title: planTitle, category: planCategory,
      frequencyDays: Number(frequencyDays), firstDueOn }, "Plan održavanja je kreiran.");
  }

  return <>
    <SectionTitle title="Održavanje" description="Prijave kvarova, radni nalozi i buduće servisne obaveze." />
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => { setZevId(event.target.value); setUnitId(""); }}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    {zevId && <><div className="finance-grid">{allowed("Maintenance.Create") && <section className="panel"><h2>Prijava kvara</h2>
      <form className="admin-form" onSubmit={createTicket}>
        {role === "owner" && <label>Nekretnina<select required value={unitId} onChange={(event) => setUnitId(event.target.value)}>
          <option value="">Odaberite</option>{units.map((unit) => <option key={unit.id} value={unit.id}>{unit.code}</option>)}</select></label>}
        <label>Kategorija<input required maxLength={100} value={category} onChange={(event) => setCategory(event.target.value)} /></label>
        <label>Opis<textarea required maxLength={4000} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label>Lokacija<input required maxLength={250} value={location} onChange={(event) => setLocation(event.target.value)} /></label>
        <label>Prioritet<select value={priority} onChange={(event) => setPriority(event.target.value)}>
          {['Low', 'Normal', 'High', 'Urgent'].map((item) => <option key={item}>{item}</option>)}</select></label>
        <label>Fotografija<input type="file" accept="image/png,image/jpeg" onChange={(event) => setPhoto(event.target.files?.[0] ?? null)} /></label>
        <button disabled={busy || (role === "owner" && !unitId)}>Pošalji prijavu</button>
      </form></section>}
      {role === "manager" && <>{allowed("Maintenance.Assign") && <section className="panel"><h2>Novi radni nalog</h2>
        <form className="admin-form" onSubmit={createOrder}>
          <label>Prijava<select value={orderTicketId} onChange={(event) => setOrderTicketId(event.target.value)}>
            <option value="">Bez prijave</option>{tickets.items.map((item) => <option key={item.id} value={item.id}>{item.category} · {item.location}</option>)}</select></label>
          <label>Naslov<input required maxLength={200} value={orderTitle} onChange={(event) => setOrderTitle(event.target.value)} /></label>
          <label>Opis<textarea required maxLength={4000} value={orderDescription} onChange={(event) => setOrderDescription(event.target.value)} /></label>
          <label>Trošak<input type="number" min="0" step="0.01" value={orderCost} onChange={(event) => setOrderCost(event.target.value)} /></label>
          <label>Rok<input type="date" value={orderDueOn} onChange={(event) => setOrderDueOn(event.target.value)} /></label>
          <button disabled={busy}>Kreiraj nalog</button>
        </form></section>}
        {allowed("Maintenance.Create") && <section className="panel"><h2>Periodični plan</h2><form className="admin-form" onSubmit={createPlan}>
          <label>Naslov<input required maxLength={200} value={planTitle} onChange={(event) => setPlanTitle(event.target.value)} /></label>
          <label>Kategorija<input required maxLength={100} value={planCategory} onChange={(event) => setPlanCategory(event.target.value)} /></label>
          <label>Interval (dani)<input required type="number" min="1" value={frequencyDays} onChange={(event) => setFrequencyDays(event.target.value)} /></label>
          <label>Prvi termin<input required type="date" value={firstDueOn} onChange={(event) => setFirstDueOn(event.target.value)} /></label>
          <button disabled={busy}>Dodaj plan</button></form></section>}</>}
    </div>
    <section className="panel table-panel"><h2>Prijave kvarova</h2><div className="table-scroll"><table><thead><tr>
      <th>Kategorija</th><th>Lokacija</th><th>Opis</th><th>Prioritet</th><th>Status</th><th>Radnja</th>
    </tr></thead><tbody>{tickets.items.map((item) => <tr key={item.id}><td>{item.category}</td><td>{item.location}</td>
      <td>{item.description}</td><td>{item.priority}</td><td>{item.status}</td><td className="actions">
        {role !== "owner" && (allowed("Maintenance.Edit") || allowed("Maintenance.Approve")) && item.status === "Open" && <button onClick={() => changeStatus("tickets", item, "InProgress")}>Započni</button>}
        {role !== "owner" && (allowed("Maintenance.Edit") || allowed("Maintenance.Approve")) && item.status === "InProgress" && <button onClick={() => changeStatus("tickets", item, "Completed")}>Završi</button>}
        {item.status === "Completed" && (role === "owner" || allowed("Maintenance.Approve")) && <button onClick={() => changeStatus("tickets", item, "Verified")}>Potvrdi</button>}
      </td></tr>)}</tbody></table></div><Pager {...tickets} onPage={setTicketPage} /></section>
    {role !== "owner" && <><section className="panel table-panel"><h2>Radni nalozi</h2><div className="table-scroll"><table><thead><tr>
      <th>Naslov</th><th>Rok</th><th>Trošak</th><th>Status</th><th>Radnja</th>
    </tr></thead><tbody>{orders.items.map((item) => <tr key={item.id}><td>{item.title}</td><td>{item.dueOn ?? "—"}</td>
      <td>{item.costAmount?.toFixed(2) ?? "—"}</td><td>{item.status}</td><td className="actions">
        {role === "manager" && allowed("Maintenance.Edit") && item.status === "Open" && <button onClick={() => changeStatus("work-orders", item, "InProgress")}>Započni</button>}
        {role === "manager" && allowed("Maintenance.Edit") && item.status === "InProgress" && <button onClick={() => changeStatus("work-orders", item, "Completed")}>Završi</button>}
      </td></tr>)}</tbody></table></div></section>
      <section className="panel table-panel"><h2>Kalendar održavanja</h2>
        <div className="table-scroll"><table><thead><tr><th>Obaveza</th><th>Datum</th><th>Status</th></tr></thead><tbody>
          {calendar.map((item) => <tr key={item.id}><td>{item.title}</td><td>{item.dueOn}</td><td>{item.status}</td></tr>)}
        </tbody></table></div><p className="empty-inline">Aktivnih planova: {plans.length}</p></section></>}
    </>}
  </>;
}
