import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";
import { formatMoney } from "../currency.ts";

type ZevOption = { id: string; code: string; name: string };
type Fund = { id: string; name: string };
type Supplier = { id: string; name: string };
type Expense = { id: string; supplierName: string; invoiceNumber: string; invoiceDate: string;
  amount: number; currencyCode: string; category: string; status: string; documentId: string | null };
type Debtor = { personId: string; personName: string; currencyCode: string;
  outstanding: number; oldestDue: string; openItems: number };

export default function ExpensesPage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [funds, setFunds] = useState<Fund[]>([]);
  const [suppliers, setSuppliers] = useState<Page<Supplier>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [expenses, setExpenses] = useState<Page<Expense>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [debtors, setDebtors] = useState<Page<Debtor>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [expensePage, setExpensePage] = useState(1);
  const [debtorPage, setDebtorPage] = useState(1);
  const [supplierName, setSupplierName] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [fundId, setFundId] = useState("");
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState("");
  const [amount, setAmount] = useState("");
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:expenses`, `zev:${zevId}:finance`);

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const [fundData, supplierData, expenseData, debtorData] = await Promise.all([
        allowed("Funds.View") ? callApi<Fund[]>(`/api/zevs/${zevId}/finance/funds`, {}, token) : Promise.resolve([]),
        callApi<Page<Supplier>>(`/api/zevs/${zevId}/expenses/suppliers?pageSize=100`, {}, token),
        callApi<Page<Expense>>(`/api/zevs/${zevId}/expenses?page=${expensePage}`, {}, token),
        callApi<Page<Debtor>>(`/api/zevs/${zevId}/expenses/debtors?page=${debtorPage}`, {}, token),
      ]);
      if (currentRequest === requestId.current) {
        if (expensePage > 1 && expenseData.items.length === 0) { setExpensePage(Math.max(1, Math.ceil(expenseData.totalCount / 20))); return; }
        if (debtorPage > 1 && debtorData.items.length === 0) { setDebtorPage(Math.max(1, Math.ceil(debtorData.totalCount / 20))); return; }
        setFunds(fundData); setSuppliers(supplierData); setExpenses(expenseData); setDebtors(debtorData); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, expensePage, debtorPage, token, permissions]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function createSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return; pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/expenses/suppliers`, { method: "POST",
        body: JSON.stringify({ name: supplierName }) }, token);
      setSupplierName(""); setNotice("Dobavljač je kreiran.");
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function createExpense(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return; pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      let documentId: string | null = null;
      if (file) {
        if (file.size > 5_000_000) throw new Error("Datoteka je veća od 5 MB.");
        const contentBase64 = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
          reader.onerror = () => reject(reader.error);
          reader.readAsDataURL(file);
        });
        const uploaded = await callApi<{ id: string }>(`/api/zevs/${zevId}/files`, {
          method: "POST", body: JSON.stringify({ fileName: file.name, contentType: file.type, contentBase64 }),
        }, token);
        documentId = uploaded.id;
      }
      await callApi(`/api/zevs/${zevId}/expenses`, { method: "POST", body: JSON.stringify({ fundId,
        supplierId, invoiceNumber, invoiceDate, amount: Number(amount), category, description, documentId }),
      }, token);
      setNotice("Rashod je sačuvan kao nacrt. Knjiženje je posebna radnja.");
      setInvoiceNumber(""); setAmount(""); setFile(null);
    } catch (problem) { setError(problem instanceof Error && problem.message.includes("5 MB")
      ? problem.message : errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function changeStatus(item: Expense, action: "post" | "reverse") {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/expenses/${item.id}/${action}`, { method: "POST" }, token);
      setNotice(action === "post" ? "Rashod je knjižen." : "Rashod je storniran.");
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  return <>
    <SectionTitle title="Rashodi i dugovanja" description="Ulazni računi, stanje fondova i pregled otvorenih dugovanja." />
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => setZevId(event.target.value)}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    {zevId && <><div className="admin-layout">
      {allowed("Expenses.Create") && <section className="panel"><h2>Dobavljač</h2><form className="admin-form" onSubmit={createSupplier}>
        <label>Naziv<input required maxLength={200} value={supplierName} onChange={(event) => setSupplierName(event.target.value)} /></label>
        <button disabled={busy}>Dodaj dobavljača</button></form></section>}
      {allowed("Expenses.Create") && <section className="panel"><h2>Novi ulazni račun</h2><form className="admin-form" onSubmit={createExpense}>
        <label>Fond<select required value={fundId} onChange={(event) => setFundId(event.target.value)}>
          <option value="">Odaberite</option>{funds.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Dobavljač<select required value={supplierId} onChange={(event) => setSupplierId(event.target.value)}>
          <option value="">Odaberite</option>{suppliers.items.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Broj računa<input required maxLength={100} value={invoiceNumber} onChange={(event) => setInvoiceNumber(event.target.value)} /></label>
        <label>Datum<input required type="date" value={invoiceDate} onChange={(event) => setInvoiceDate(event.target.value)} /></label>
        <label>Iznos<input required type="number" min="0.01" step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} /></label>
        <label>Kategorija<input required maxLength={100} value={category} onChange={(event) => setCategory(event.target.value)} /></label>
        <label>Opis<input maxLength={1000} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
        <label>Prilog (PDF, PNG, JPG; do 5 MB)<input type="file" accept=".pdf,.png,.jpg,.jpeg"
          onChange={(event) => setFile(event.target.files?.[0] ?? null)} /></label>
        <button disabled={busy}>Sačuvaj nacrt</button></form></section>}
    </div>
    <section className="panel table-panel"><h2>Rashodi</h2><div className="table-scroll"><table><thead><tr>
      <th>Dobavljač</th><th>Račun</th><th>Datum</th><th>Kategorija</th><th>Iznos</th><th>Status</th><th>Radnja</th>
    </tr></thead><tbody>{expenses.items.map((item) => <tr key={item.id}><td>{item.supplierName}</td><td>{item.invoiceNumber}</td>
      <td>{item.invoiceDate}</td><td>{item.category}</td><td>{formatMoney(item.amount, item.currencyCode)}</td><td>{item.status}</td>
      <td className="actions">{allowed("Expenses.Post") && item.status === "Draft" && <button disabled={busy} onClick={() => changeStatus(item, "post")}>Knjiži</button>}
        {allowed("Expenses.Reverse") && item.status === "Posted" && <button disabled={busy} onClick={() => changeStatus(item, "reverse")}>Storniraj</button>}
        {item.documentId && <button type="button" onClick={async () => {
          try {
            const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/files/${item.documentId}`,
              { headers: { Authorization: `Bearer ${token}` } });
            if (!response.ok) throw new Error();
            const url = URL.createObjectURL(await response.blob()); window.open(url, "_blank", "noopener,noreferrer");
            setTimeout(() => URL.revokeObjectURL(url), 60_000);
          } catch { setError("Prilog nije moguće otvoriti."); }
        }}>Prilog</button>}</td></tr>)}</tbody></table></div><Pager {...expenses} onPage={setExpensePage} /></section>
    <section className="panel table-panel"><h2>Dužnici</h2><div className="table-scroll"><table><thead><tr>
      <th>Lice</th><th>Otvoreno</th><th>Najstarije dospijeće</th><th>Stavke</th>
    </tr></thead><tbody>{debtors.items.map((item) => <tr key={`${item.personId}-${item.currencyCode}`}><td>{item.personName}</td>
      <td>{formatMoney(item.outstanding, item.currencyCode)}</td><td>{item.oldestDue}</td><td>{item.openItems}</td></tr>)}</tbody></table></div>
      {debtors.items.length === 0 && <p className="empty-inline">Nema dospjelih otvorenih stavki.</p>}
      <Pager {...debtors} onPage={setDebtorPage} /></section></>}
  </>;
}
