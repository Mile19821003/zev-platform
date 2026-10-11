import { ChangeEvent, FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { useChangeRevision } from "../useChangeRevision.ts";
import { canShowPaymentAction } from "../permissionUi.ts";
import PaymentWorkspace from "./PaymentWorkspace";
import { formatMoney } from "../currency.ts";
import { formatDate } from "../bankBalanceUi.ts";
import BankAccountBalanceCard from "./BankAccountBalanceCard";
import { bankImportStatusLabels, bankTransactionStatusLabels, label } from "../uiLabels.ts";

type ZevOption = { id: string; code: string; name: string };
type Fund = { id: string; name: string };
type Account = { id: string; fundId: string; accountNumber: string; currencyCode: string };
type QueueItem = { id: string; bookingDate: string; amount: number; reference: string | null;
  counterparty: string | null; description: string | null; status: string; currencyCode: string };
type Statement = { id: string; bankAccountId: string; originalFileName: string; format: string; transactionCount: number;
  status: string; importedByUserId: string | null; importedUtc: string; openingBalance: number | null;
  closingBalance: number | null; statementFrom: string | null; statementTo: string | null };
type Preview = { transactionCount: number; isDuplicate: boolean; truncated: boolean;
  currencyCode: string; openingBalance: number | null; closingBalance: number | null;
  statementFrom: string | null; statementTo: string | null;
  items: { bookingDate: string; amount: number; reference: string | null; counterparty: string | null }[] };
type Payment = { id: string; paidOn: string; amount: number; bankTransactionId: string | null;
  status: string; currencyCode: string };

export default function PaymentsPage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const allowed = (key: string) => canShowPaymentAction(permissions, key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [funds, setFunds] = useState<Fund[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [fundId, setFundId] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [bankName, setBankName] = useState("");
  const [currencyCode, setCurrencyCode] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [balanceAccountId, setBalanceAccountId] = useState("");
  const [format, setFormat] = useState("CSV");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [queue, setQueue] = useState<Page<QueueItem>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [statements, setStatements] = useState<Page<Statement>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [payments, setPayments] = useState<Page<Payment>>({ page: 1, pageSize: 100, totalCount: 0, items: [] });
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<QueueItem | null>(null);
  const [returnPaymentId, setReturnPaymentId] = useState("");
  const [returnReason, setReturnReason] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:payments`, `zev:${zevId}:finance`);

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const [fundData, accountData, queueData, statementData, paymentData] = await Promise.all([
        allowed("Funds.View") ? callApi<Fund[]>(`/api/zevs/${zevId}/finance/funds`, {}, token) : Promise.resolve([]),
        allowed("BankAccounts.View") ? callApi<Account[]>(`/api/zevs/${zevId}/payments/accounts`, {}, token) : Promise.resolve([]),
        callApi<Page<QueueItem>>(`/api/zevs/${zevId}/payments/reconciliation-queue?returnsOnly=true&page=${page}`, {}, token),
        allowed("BankStatements.View") ? callApi<Page<Statement>>(`/api/zevs/${zevId}/payments/statements`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
        callApi<Page<Payment>>(`/api/zevs/${zevId}/payments/payments?pageSize=100`, {}, token),
      ]);
      if (currentRequest === requestId.current) {
        if (page > 1 && queueData.items.length === 0) { setPage(Math.max(1, Math.ceil(queueData.totalCount / 20))); return; }
        setFunds(fundData); setAccounts(accountData); setQueue(queueData); setStatements(statementData);
        setBalanceAccountId((current) => accountData.some((account) => account.id === current) ? current
          : accountData[0]?.id ?? "");
        setPayments(paymentData); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, page, token, permissions]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function createAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return; pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/payments/accounts`, { method: "POST", body: JSON.stringify({
        fundId, accountNumber, currencyCode, bankName,
      }) }, token);
      setNotice("Račun je kreiran."); setAccountNumber(""); setBankName(""); setCurrencyCode("");
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function importFile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current) return;
    if (!file || file.size > 5_000_000 || !preview || preview.isDuplicate) {
      setError("Prvo pregledajte novi izvod do 5 MB."); return;
    }
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const contentBase64 = await readBase64(file);
      const result = await callApi<{ transactionCount: number; matchedCount: number }>(
        `/api/zevs/${zevId}/payments/statements`, { method: "POST", body: JSON.stringify({
          bankAccountId, fileName: file.name, format, contentBase64,
        }) }, token);
      setNotice(`Uvezeno ${result.transactionCount} transakcija; automatski povezano ${result.matchedCount}.`);
      setFile(null); setPreview(null);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function previewFile() {
    if (pending.current) return;
    if (!file || file.size > 5_000_000 || !bankAccountId) {
      setError("Odaberite račun i datoteku do 5 MB."); return;
    }
    pending.current = true; setBusy(true); setError(""); setPreview(null);
    try {
      const result = await callApi<Preview>(`/api/zevs/${zevId}/payments/statements/preview`, {
        method: "POST", body: JSON.stringify({ bankAccountId, fileName: file.name, format,
          contentBase64: await readBase64(file) }),
      }, token);
      setPreview(result);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function confirmReturn() {
    if (pending.current || !selected || selected.amount >= 0 || !returnPaymentId || !returnReason) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/payments/bank-transactions/${selected.id}/return`, {
        method: "POST", body: JSON.stringify({ paymentId: returnPaymentId, reason: returnReason }),
      }, token);
      setNotice("Puni povrat je evidentiran; originalna uplata i raspodjele ostaju u auditu.");
      setSelected(null); setReturnPaymentId(""); setReturnReason("");
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function exportPayments() {
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/payments/export`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Izvoz uplata nije dostupan.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = "uplate.csv";
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
  }

  return <>
    <SectionTitle title="Uplate i izvodi" description="Uvoz, automatsko i ručno sravnjenje bankovnih transakcija." />
    {allowed("Payments.Export") && <button type="button" onClick={() => void exportPayments()}>Export uplata</button>}
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => { setZevId(event.target.value); setPreview(null); setBankAccountId(""); setBalanceAccountId(""); }}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    {zevId && <><PaymentWorkspace zevId={zevId} token={token} permissions={permissions}
      funds={funds} accounts={accounts} /><div className="admin-layout">
      <section className="panel"><h2>Bankovni račun</h2>{allowed("BankAccounts.View") && <div className="table-scroll"><table>
        <thead><tr><th>Broj računa</th><th>Valuta</th><th>Stanje</th></tr></thead><tbody>{accounts.map((account) =>
          <tr key={account.id} className={balanceAccountId === account.id ? "selected" : ""}><td>{account.accountNumber}</td>
            <td>{account.currencyCode}</td><td><button type="button" aria-pressed={balanceAccountId === account.id}
              aria-label={`Prikaži stanje računa ${account.accountNumber}`}
              onClick={() => setBalanceAccountId(account.id)}>Prikaži</button></td></tr>)}</tbody></table></div>}
        {allowed("BankAccounts.Create") && <form className="admin-form" onSubmit={createAccount}>
        <label>Fond<select required value={fundId} onChange={(event) => setFundId(event.target.value)}>
          <option value="">Odaberite</option>{funds.map((fund) => <option key={fund.id} value={fund.id}>{fund.name}</option>)}</select></label>
        <label>Broj računa<input required inputMode="numeric" maxLength={19} pattern="[0-9 \-]{16,19}"
          placeholder="16 cifara, npr. 1610000012345678" title="Transakcijski račun: 16 cifara (BiH standard)"
          value={accountNumber} onChange={(event) => setAccountNumber(event.target.value)} /></label>
        <label>Banka<input maxLength={120} value={bankName} onChange={(event) => setBankName(event.target.value)} /></label>
        <label>Valuta (ISO 4217)<input required minLength={3} maxLength={3} value={currencyCode} onChange={(event) => setCurrencyCode(event.target.value.toUpperCase())} /></label>
        <button type="submit" disabled={busy}>Dodaj račun</button></form>}</section>
      {(allowed("BankStatements.Preview") || allowed("BankStatements.Import") && allowed("BankStatements.Post")) && <section className="panel"><h2>Uvoz izvoda</h2>
        <form className="admin-form" onSubmit={importFile}>
        <label>Račun<select required value={bankAccountId} onChange={(event) => { setBankAccountId(event.target.value); setPreview(null); }}>
          <option value="">Odaberite</option>{accounts.map((account) => <option key={account.id} value={account.id}>
            {account.accountNumber} · {account.currencyCode}</option>)}</select></label>
        <label>Format<select value={format} onChange={(event) => { setFormat(event.target.value); setPreview(null); }}>
          <option>CSV</option><option>TXT</option><option>XML</option><option>CAMT053</option></select></label>
        <label>Datoteka<input required type="file" accept=".csv,.txt,.xml" onChange={(event: ChangeEvent<HTMLInputElement>) =>
          { setFile(event.target.files?.[0] ?? null); setPreview(null); }} /></label>
        {allowed("BankStatements.Preview") && <button type="button" disabled={busy || !file || !bankAccountId}
          onClick={() => void previewFile()}>Pregledaj bez knjiženja</button>}
        {allowed("BankStatements.Import") && allowed("BankStatements.Post") && allowed("Payments.ReconcileAutomatic")
          && <button type="submit" disabled={busy || !preview || preview.isDuplicate}>Uvezi izvod</button>}
        </form>
        {preview && <div><p role="status">{preview.transactionCount} transakcija · {preview.isDuplicate ? "Duplikat izvoda" : "Spremno za uvoz"}</p>
          <p>{preview.openingBalance !== null || preview.closingBalance !== null
            ? `Saldo izvoda: ${preview.openingBalance !== null ? formatMoney(preview.openingBalance, preview.currencyCode) : "—"} → ${preview.closingBalance !== null ? formatMoney(preview.closingBalance, preview.currencyCode) : "—"}`
            : "Izvod ne sadrži saldo."}{preview.statementFrom && preview.statementTo
            ? ` · period ${formatDate(preview.statementFrom)} – ${formatDate(preview.statementTo)}` : ""}</p>
          <div className="table-wrap"><table><thead><tr><th>Datum</th><th>Iznos</th><th>Poziv</th></tr></thead><tbody>
            {preview.items.map((item, index) => <tr key={index}><td>{item.bookingDate}</td>
              <td>{formatMoney(item.amount, preview.currencyCode)}</td>
              <td>{item.reference ?? "—"}</td></tr>)}</tbody></table></div>
          {preview.truncated && <p>Prikazano prvih 50 transakcija.</p>}</div>}</section>}
    </div>
    {allowed("BankAccounts.View") && balanceAccountId && <section className="panel">
      <h2>Stanje računa i usklađivanje · {accounts.find((account) => account.id === balanceAccountId)?.accountNumber}</h2>
      <BankAccountBalanceCard token={token} zevId={zevId} accountId={balanceAccountId} revision={revision} /></section>}
    <section className="panel table-panel"><h2>Povrati za ručnu potvrdu</h2>
      <div className="table-scroll"><table><thead><tr><th>Datum</th><th>Iznos</th><th>Poziv</th><th>Uplatilac</th><th>Status</th><th>Radnja</th></tr></thead>
        <tbody>{queue.items.map((item) => <tr key={item.id}><td>{item.bookingDate}</td>
          <td>{formatMoney(item.amount, item.currencyCode)}</td>
          <td>{item.reference ?? "—"}</td><td>{item.counterparty ?? "—"}</td><td>{label(bankTransactionStatusLabels, item.status)}</td><td className="actions">
            {allowed("Payments.Reverse") && <button type="button"
              onClick={() => setSelected(item)}>Povrat</button>}</td></tr>)}</tbody></table></div>
      {queue.items.length === 0 && <p className="empty-inline">Nema povrata za ručni pregled.</p>}
      <Pager {...queue} onPage={setPage} />
    </section>
    {selected && selected.amount < 0 && <section className="panel manual-panel"><h2>Puni povrat · {formatMoney(selected.amount, selected.currencyCode)}</h2>
      <p>Potvrdite samo ako je negativna transakcija puni povrat originalne uplate na istom bankovnom računu.</p>
      <label>Originalna uplata<select required value={returnPaymentId} onChange={(event) => setReturnPaymentId(event.target.value)}>
        <option value="">Odaberite uplatu</option>{payments.items.filter((item) => item.status === "Posted" && item.bankTransactionId)
          .map((item) => <option key={item.id} value={item.id}>{item.paidOn} · {formatMoney(item.amount, item.currencyCode)} · {item.bankTransactionId}</option>)}
      </select></label>
      <label>Razlog<input required maxLength={250} value={returnReason} onChange={(event) => setReturnReason(event.target.value)} /></label>
      <button type="button" disabled={busy || !returnPaymentId || !returnReason} onClick={() => void confirmReturn()}>
        {busy ? "Knjiženje u toku..." : "Potvrdi puni povrat"}</button>
    </section>}
    <section className="panel table-panel"><h2>Uvezeni izvodi</h2>
      <div className="table-scroll"><table><thead><tr><th>Datoteka</th><th>Format</th><th>Period</th><th>Početni saldo</th><th>Završni saldo</th><th>Transakcije</th><th>Status</th><th>Datum</th><th>Importer ID</th></tr></thead>
        <tbody>{statements.items.map((item) => { const currency = accounts.find((account) => account.id === item.bankAccountId)?.currencyCode ?? "BAM";
          return <tr key={item.id}><td>{item.originalFileName}</td><td>{item.format}</td>
          <td>{item.statementFrom && item.statementTo ? `${formatDate(item.statementFrom)} – ${formatDate(item.statementTo)}` : "—"}</td>
          <td>{item.openingBalance !== null ? formatMoney(item.openingBalance, currency) : "—"}</td>
          <td>{item.closingBalance !== null ? formatMoney(item.closingBalance, currency) : "—"}</td>
          <td>{item.transactionCount}</td><td>{label(bankImportStatusLabels, item.status)}</td><td>{item.importedUtc}</td>
          <td>{item.importedByUserId ?? "—"}</td></tr>; })}</tbody></table></div>
    </section>
    </>}
  </>;
}

function readBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
