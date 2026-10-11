import { useEffect, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import PaymentWorkspace from "./PaymentWorkspace";
import { allocationStatusLabel, matchRuleLabel } from "../paymentUi.ts";
import { formatMoney } from "../currency.ts";
import { bankTransactionStatusLabels } from "../uiLabels.ts";

type Zev = { id: string; name: string; code: string };
type Account = { id: string; accountNumber: string };
type Fund = { id: string; name: string };
type Person = { id: string; displayName: string };
type BankRow = { id: string; bookingDate: string; accountNumber: string; counterparty: string | null;
  description: string | null; reference: string | null; amount: number; status: string; currencyCode: string;
  allocationStatus: string;
  payment: { id: string; personName: string | null; allocated: number } | null; rule: string | null;
  remaining: number };

export default function BankTransactionsPage({ token, zevs, permissions }: { token: string;
  zevs: Zev[]; permissions?: string[] }) {
  const zevId = zevs[0]?.id ?? "";
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [funds, setFunds] = useState<Fund[]>([]);
  const [people, setPeople] = useState<Page<Person>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [units, setUnits] = useState<{ id: string; code: string }[]>([]);
  const [rows, setRows] = useState<Page<BankRow>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [page, setPage] = useState(1);
  const [accountId, setAccountId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [status, setStatus] = useState("");
  const [personId, setPersonId] = useState("");
  const [propertyUnitId, setPropertyUnitId] = useState("");
  const [personSearch, setPersonSearch] = useState("");
  const [reference, setReference] = useState("");
  const [amount, setAmount] = useState("");
  const [search, setSearch] = useState("");
  const [selectedPaymentId, setSelectedPaymentId] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    if (!zevId) return;
    void callApi<{ accounts: Account[]; funds: Fund[] }>(`/api/zevs/${zevId}/payments/lookup`, {}, token)
      .then((lookup) => { setAccounts(lookup.accounts); setFunds(lookup.funds); })
      .catch((problem) => setError(errorText(problem)));
  }, [zevId, token]);
  useEffect(() => {
    if (!zevId) return;
    void callApi<Page<Person>>(`/api/zevs/${zevId}/payments/people?search=${encodeURIComponent(personSearch)}&pageSize=50`,
      {}, token).then(setPeople).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, personSearch]);
  useEffect(() => {
    if (!personId || !zevId) { setUnits([]); return; }
    void callApi<{ id: string; code: string }[]>(`/api/zevs/${zevId}/payments/units?personId=${personId}`, {}, token)
      .then(setUnits).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, personId]);
  useEffect(() => {
    if (!zevId) return;
    const params = new URLSearchParams({ page: String(page), pageSize: "20" });
    for (const [key, value] of Object.entries({ bankAccountId: accountId, from, to, status,
      personId, propertyUnitId,
      reference, amount, search })) if (value) params.set(key, value);
    void callApi<Page<BankRow>>(`/api/zevs/${zevId}/payments/bank-transactions?${params}`, {}, token)
      .then(setRows).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, page, accountId, from, to, status, personId, propertyUnitId,
    reference, amount, search]);
  return <><SectionTitle title="Bankovne transakcije" description="Izvodi, prepoznavanje i sravnjenje." />
    {error && <p className="error" role="alert">{error}</p>}
    <section className="panel table-panel"><div className="admin-form">
      <label>Račun<select value={accountId} onChange={(event) => { setAccountId(event.target.value); setPage(1); }}>
        <option value="">Svi</option>{accounts.map((item) => <option key={item.id} value={item.id}>{item.accountNumber}</option>)}</select></label>
      <label>Od<input type="date" value={from} onChange={(event) => { setFrom(event.target.value); setPage(1); }} /></label>
      <label>Do<input type="date" value={to} onChange={(event) => { setTo(event.target.value); setPage(1); }} /></label>
      <label>Status<select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }}>
        <option value="">Svi</option>{Object.entries(bankTransactionStatusLabels)
          .map(([code, name]) => <option key={code} value={code}>{name}</option>)}</select></label>
      <label>Traži vlasnika<input value={personSearch} onChange={(event) => setPersonSearch(event.target.value)} /></label>
      <label>Vlasnik<select value={personId} onChange={(event) => { setPersonId(event.target.value);
        setPropertyUnitId(""); setPage(1); }}><option value="">Svi</option>{people.items.map((item) =>
          <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
      <label>Jedinica<select value={propertyUnitId} onChange={(event) => { setPropertyUnitId(event.target.value);
        setPage(1); }}><option value="">Sve</option>{units.map((item) =>
          <option key={item.id} value={item.id}>{item.code}</option>)}</select></label>
      <label>Poziv<input value={reference} onChange={(event) => { setReference(event.target.value); setPage(1); }} /></label>
      <label>Iznos<input type="number" step="0.01" value={amount} onChange={(event) => { setAmount(event.target.value); setPage(1); }} /></label>
      <label>Tekst<input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></label>
    </div><div className="table-scroll"><table><thead><tr><th>Datum</th><th>Račun</th><th>Uplatilac</th>
      <th>Opis</th><th>Poziv</th><th>Iznos</th><th>Status</th><th>Vlasnik</th>
      <th>Raspoređeno</th><th>Preostalo</th><th>Pravilo</th><th>Radnja</th></tr></thead>
      <tbody>{rows.items.map((item) => <tr key={item.id}><td>{item.bookingDate}</td><td>{item.accountNumber}</td>
        <td>{item.counterparty ?? "—"}</td><td>{item.description ?? "—"}</td><td>{item.reference ?? "—"}</td>
        <td>{formatMoney(item.amount, item.currencyCode)}</td><td>{allocationStatusLabel(item.allocationStatus)}</td>
        <td>{item.payment?.personName ?? "Neprepoznat"}</td>
        <td>{formatMoney(item.payment?.allocated ?? 0, item.currencyCode)}</td>
        <td>{formatMoney(item.remaining, item.currencyCode)}</td>
        <td>{item.rule ? matchRuleLabel(item.rule) : "—"}</td><td>{item.payment && <button type="button"
          onClick={() => setSelectedPaymentId(item.payment!.id)}>Otvori sravnjenje</button>}</td></tr>)}</tbody></table></div>
      {rows.items.length === 0 && <p className="empty-inline">Nema bankovnih transakcija za zadate filtere.</p>}
      <Pager {...rows} onPage={setPage} />
    </section>
    {selectedPaymentId && <PaymentWorkspace zevId={zevId} token={token} permissions={permissions}
      accounts={accounts} funds={funds} initialPaymentId={selectedPaymentId} />}
  </>;
}
