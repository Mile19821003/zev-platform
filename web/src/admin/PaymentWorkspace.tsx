import { useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager } from "./shared";
import { canShowPaymentAction } from "../permissionUi.ts";
import { activeAllocationCount, allocationStatusLabel, canSubmitPayerCorrection,
  matchRuleLabel } from "../paymentUi.ts";
import { formatMoney } from "../currency.ts";

type Fund = { id: string; name: string };
type Account = { id: string; accountNumber: string };
type Person = { id: string; displayName: string };
type Payment = { id: string; personId: string | null; personName: string | null; fundId: string;
  paidOn: string; amount: number; allocated: number; unallocated: number; currencyCode: string;
  status: string; sourceType: string; bankStatus: string | null; bankTransactionId: string | null;
  matchRule: string | null; allocationStatus: string;
  reference: string | null; counterparty: string | null; description: string | null };
type Charge = { id: string; personId: string; propertyUnitId: string; fundId: string;
  personName: string; unitCode: string; period: string; dueDate: string; reference: string;
  invoiceNumber: string | null; open: number };
type Allocation = { id: string; chargeId: string; amount: number; reversesAllocationId: string | null;
  financialDocumentId: string | null; reversalReason: string | null; unitCode: string;
  period: string; invoiceNumber: string | null; description: string | null; releaseInvoiceNumber: string | null };
type Detail = { payment: Payment & { manualReason: string | null; manualMethod: string | null };
  allocated: number; credit: number; bankTransaction: { reference: string | null; counterparty: string | null;
    description: string | null; rule: string | null } | null; allocations: Allocation[];
  identificationHistory: { id: string; previousPersonName: string | null; newPersonName: string;
    reason: string; changedUtc: string; actorName: string | null }[] };
type AutoPreview = { available: number; allocated: number; lines: { chargeId: string; period: string;
  dueDate: string; invoiceNumber: string | null; amount: number }[] };

const emptyPage = <Item,>(): Page<Item> => ({ page: 1, pageSize: 20, totalCount: 0, items: [] });

export default function PaymentWorkspace({ zevId, token, permissions, funds, accounts, initialPaymentId }: {
  zevId: string; token: string; permissions?: string[]; funds: Fund[]; accounts: Account[];
  initialPaymentId?: string }) {
  const allowed = (key: string) => canShowPaymentAction(permissions, key);
  const [payments, setPayments] = useState<Page<Payment>>(emptyPage());
  const [lookup, setLookup] = useState<{ funds: Fund[]; accounts: Account[] }>({ funds: [], accounts: [] });
  const [people, setPeople] = useState<Page<Person>>(emptyPage());
  const [unitOptions, setUnitOptions] = useState<{ id: string; code: string }[]>([]);
  const [charges, setCharges] = useState<Page<Charge>>(emptyPage());
  const [page, setPage] = useState(1);
  const [chargePage, setChargePage] = useState(1);
  const [filterStatus, setFilterStatus] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [personSearch, setPersonSearch] = useState("");
  const [personFilter, setPersonFilter] = useState("");
  const [unitFilter, setUnitFilter] = useState("");
  const [amountFilter, setAmountFilter] = useState("");
  const [reference, setReference] = useState("");
  const [search, setSearch] = useState("");
  const [chargeSearch, setChargeSearch] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [detail, setDetail] = useState<Detail | null>(null);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [autoPreview, setAutoPreview] = useState<AutoPreview | null>(null);
  const [reverseReason, setReverseReason] = useState("");
  const [changePayerOpen, setChangePayerOpen] = useState(false);
  const [newPayerId, setNewPayerId] = useState("");
  const [payerReason, setPayerReason] = useState("");
  const [payerWarningAccepted, setPayerWarningAccepted] = useState(false);
  const [manualOpen, setManualOpen] = useState(false);
  const [manualPersonId, setManualPersonId] = useState("");
  const [manualFundId, setManualFundId] = useState("");
  const [manualUnitId, setManualUnitId] = useState("");
  const [manualDate, setManualDate] = useState(new Date().toISOString().slice(0, 10));
  const [manualAmount, setManualAmount] = useState("");
  const [manualCurrency, setManualCurrency] = useState("BAM");
  const [manualMethod, setManualMethod] = useState("");
  const [manualReason, setManualReason] = useState("");
  const [manualReference, setManualReference] = useState("");
  const [manualDescription, setManualDescription] = useState("");
  const [manualDraft, setManualDraft] = useState<Record<string, string>>({});
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const [creditPolicy, setCreditPolicy] = useState<{ autoApplyPaymentCredit: boolean;
    allowManualCash: boolean; rowVersion: string } | null>(null);
  const requestKey = useRef<{ signature: string; id: string } | null>(null);
  const fundChoices = funds.length > 0 ? funds : lookup.funds;
  const accountChoices = accounts.length > 0 ? accounts : lookup.accounts;
  useEffect(() => { if (initialPaymentId) setSelectedId(initialPaymentId); }, [initialPaymentId]);

  const load = useCallback(async () => {
    if (!zevId) return;
    const params = new URLSearchParams({ page: String(page), pageSize: "20" });
    for (const [key, value] of Object.entries({ filterStatus, bankAccountId, from, to,
      personId: personFilter, propertyUnitId: unitFilter, amount: amountFilter, reference, search }))
      if (value) params.set(key, value);
    const result = await callApi<Page<Payment>>(`/api/zevs/${zevId}/payments/payments?${params}`, {}, token);
    setPayments(result);
  }, [zevId, token, page, filterStatus, bankAccountId, from, to, personFilter, unitFilter,
    amountFilter, reference, search]);

  useEffect(() => { void load().catch((problem) => setError(errorText(problem))); }, [load, revision]);
  useEffect(() => {
    if (!zevId) return;
    void callApi<{ funds: Fund[]; accounts: Account[] }>(`/api/zevs/${zevId}/payments/lookup`, {}, token)
      .then(setLookup).catch((problem) => setError(errorText(problem)));
  }, [zevId, token]);
  useEffect(() => {
    if (!zevId) return;
    void callApi<{ autoApplyPaymentCredit: boolean; allowManualCash: boolean; rowVersion: string }>(
      `/api/zevs/${zevId}/payments/credit-policy`, {}, token)
      .then(setCreditPolicy).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, revision]);
  useEffect(() => {
    if (!zevId) return;
    void callApi<Page<Person>>(`/api/zevs/${zevId}/payments/people?search=${encodeURIComponent(personSearch)}&pageSize=50`,
      {}, token).then(setPeople).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, personSearch, revision]);
  useEffect(() => {
    const personId = manualOpen ? manualPersonId : personFilter;
    if (!zevId || !personId) { setUnitOptions([]); return; }
    void callApi<{ id: string; code: string }[]>(`/api/zevs/${zevId}/payments/units?personId=${personId}`,
      {}, token).then(setUnitOptions).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, manualOpen, manualPersonId, personFilter]);
  useEffect(() => {
    if (!zevId || !selectedId) { setDetail(null); return; }
    void callApi<Detail>(`/api/zevs/${zevId}/payments/${selectedId}/details`, {}, token)
      .then(setDetail).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, selectedId, revision]);
  useEffect(() => {
    const personId = selectedId ? detail?.payment.personId : manualOpen ? manualPersonId : null;
    const fundId = selectedId ? detail?.payment.fundId : manualOpen ? manualFundId : null;
    if (!zevId || !personId || !fundId) { setCharges(emptyPage()); return; }
    const params = new URLSearchParams({ personId, fundId, page: String(chargePage), pageSize: "20",
      search: chargeSearch });
    void callApi<Page<Charge>>(`/api/zevs/${zevId}/payments/open-charges?${params}`, {}, token)
      .then(setCharges).catch((problem) => setError(errorText(problem)));
  }, [zevId, token, selectedId, detail?.payment.personId, detail?.payment.fundId,
    manualOpen, manualPersonId, manualFundId, chargePage, chargeSearch, revision]);

  async function mutate(path: string, body: unknown, message: string): Promise<boolean> {
    if (busy) return false;
    setBusy(true); setError(""); setNotice("");
    try {
      const payload = body as Record<string, unknown>;
      if (Object.hasOwn(payload, "requestId")) {
        const signature = `${path}:${JSON.stringify({ ...payload, requestId: null })}`;
        if (requestKey.current?.signature !== signature)
          requestKey.current = { signature, id: crypto.randomUUID() };
        payload.requestId = requestKey.current.id;
      }
      await callApi(`/api/zevs/${zevId}/payments${path}`,
        { method: "POST", body: JSON.stringify(payload) }, token);
      requestKey.current = null;
      setNotice(message); setRevision((current) => current + 1);
      setDraft({}); setManualDraft({}); setAutoPreview(null);
      return true;
    } catch (problem) { setError(errorText(problem)); return false; }
    finally { setBusy(false); }
  }

  function selectedLines(values: Record<string, string>) {
    return charges.items.filter((item) => values[item.id] !== undefined)
      .map((item) => ({ chargeId: item.id, amount: Number(values[item.id]) }));
  }

  async function createManual(allocate: boolean) {
    const allocations = allocate ? selectedLines(manualDraft) : [];
    if (!manualPersonId || !manualFundId || !manualDate || !manualMethod || !manualReason.trim()
      || Number(manualAmount) <= 0 || allocate && allocations.length === 0) {
      setError("Unesite vlasnika, fond, datum, pozitivan iznos, način i razlog; za raspodjelu odaberite stavke."); return;
    }
    const success = await mutate("/manual", { requestId: crypto.randomUUID(), personId: manualPersonId,
      propertyUnitId: manualUnitId || null, fundId: manualFundId, paidOn: manualDate,
      amount: Number(manualAmount), currencyCode: manualCurrency, method: manualMethod,
      reason: manualReason, reference: manualReference || null, description: manualDescription || null,
      allocations }, allocate ? "Ručna uplata je knjižena i raspoređena." : "Ručna uplata je sačuvana kao kredit.");
    if (success) { setManualOpen(false); setManualAmount(""); setManualReason(""); setManualDescription(""); }
  }

  const allocatedDraft = selectedLines(draft).reduce((sum, item) => sum + item.amount, 0);
  const activeAllocations = detail?.allocations.filter((item) => item.amount > 0
    && !detail.allocations.some((reversal) => reversal.reversesAllocationId === item.id)) ?? [];
  return <section className="panel table-panel"><h2>UPLATE · operativni pregled</h2>
    <p>Prepoznate uplate, red za provjeru, neraspoređeni kredit i korekcije alokacija na jednom mjestu.</p>
    {creditPolicy && <p>Automatska primjena kredita na nova zaduženja: <strong>{creditPolicy.autoApplyPaymentCredit ? "DA" : "NE"}</strong>
      {allowed("Payments.ReconcileAutomatic") && <button type="button" disabled={busy} onClick={() => {
        setBusy(true); setError(""); void callApi(`/api/zevs/${zevId}/payments/credit-policy`, {
          method: "PUT", body: JSON.stringify({ enabled: !creditPolicy.autoApplyPaymentCredit,
            rowVersion: creditPolicy.rowVersion }),
        }, token).then(() => { setRevision((current) => current + 1); setNotice("Politika kredita je promijenjena."); })
          .catch((problem) => setError(errorText(problem))).finally(() => setBusy(false));
      }}>{creditPolicy.autoApplyPaymentCredit ? "Isključi" : "Uključi"}</button>}</p>}
    <div className="actions">{[["", "Sve"], ["Recognized", "Automatski povezane"],
      ["Review", "Za provjeru"], ["Unrecognized", "Neprepoznate"],
      ["Partial", "Djelimično raspoređene"], ["Credit", "Preplate / krediti"]].map(([value, label]) =>
      <button type="button" key={value} aria-pressed={filterStatus === value}
        onClick={() => { setFilterStatus(value); setPage(1); }}>{label}</button>)}</div>
    <div className="admin-form">
      <label>Bankovni račun<select value={bankAccountId} onChange={(event) => { setBankAccountId(event.target.value); setPage(1); }}>
        <option value="">Svi</option>{accountChoices.map((item) => <option key={item.id} value={item.id}>{item.accountNumber}</option>)}</select></label>
      <label>Datum od<input type="date" value={from} onChange={(event) => { setFrom(event.target.value); setPage(1); }} /></label>
      <label>Datum do<input type="date" value={to} onChange={(event) => { setTo(event.target.value); setPage(1); }} /></label>
      <label>Traži vlasnika<input value={personSearch} onChange={(event) => setPersonSearch(event.target.value)} /></label>
      <label>Vlasnik<select value={personFilter} onChange={(event) => { setPersonFilter(event.target.value);
        setUnitFilter(""); setPage(1); }}>
        <option value="">Svi</option>{people.items.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
      <label>Jedinica<select value={unitFilter} onChange={(event) => { setUnitFilter(event.target.value); setPage(1); }}>
        <option value="">Sve</option>{unitOptions.map((item) => <option key={item.id} value={item.id}>{item.code}</option>)}</select></label>
      <label>Iznos<input type="number" min="0.01" step="0.01" value={amountFilter}
        onChange={(event) => { setAmountFilter(event.target.value); setPage(1); }} /></label>
      <label>Poziv<input value={reference} onChange={(event) => { setReference(event.target.value); setPage(1); }} /></label>
      <label>Tekst<input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></label>
    </div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {allowed("Payments.CreateManual") && <button type="button" onClick={() => { setManualOpen(!manualOpen); setSelectedId(""); }}>
      + NOVA RUČNA UPLATA</button>}
    <div className="table-scroll"><table><thead><tr><th>Datum</th><th>Izvor</th><th>Vlasnik</th><th>Uplatilac</th>
      <th>Poziv</th><th>Iznos</th><th>Raspoređeno</th><th>Kredit</th><th>Status</th><th>Radnja</th></tr></thead>
      <tbody>{payments.items.map((item) => <tr key={item.id}><td>{item.paidOn}</td><td>{item.sourceType}</td>
        <td>{item.personName ?? "Nepoznato"}</td><td>{item.counterparty ?? "—"}</td><td>{item.reference ?? "—"}</td>
        <td>{formatMoney(item.amount, item.currencyCode)}</td><td>{formatMoney(item.allocated, item.currencyCode)}</td>
        <td>{formatMoney(item.unallocated, item.currencyCode)}</td>
        <td>{allocationStatusLabel(item.allocationStatus)}{(item.matchRule === "EXACT_REFERENCE_V1"
          || item.matchRule === "ACCOUNT_REFERENCE_OLDEST_DUE_V1") && <small> · PREPOZNATO</small>}</td>
        <td><button type="button" onClick={() => { setSelectedId(item.id); setManualOpen(false);
          setDraft({}); setChargePage(1); }}>Otvori sravnjenje</button></td></tr>)}</tbody></table></div>
    {payments.items.length === 0 && <p className="empty-inline">Nema uplata za zadate filtere.</p>}
    <Pager {...payments} onPage={setPage} />
    {(detail || manualOpen) && <div className="manual-panel">
      {detail && <><h3>Uplata · {formatMoney(detail.payment.amount, detail.payment.currencyCode)}</h3>
        <p>Datum: {detail.payment.paidOn} · Vlasnik: {detail.payment.personName ?? "Neprepoznat"}
          {detail.bankTransaction && <> · Uplatilac: {detail.bankTransaction.counterparty ?? "—"}
            · Poziv: {detail.bankTransaction.reference ?? "—"}
            · Opis: {detail.bankTransaction.description ?? "—"}</>}</p>
        <p>Sistem je prepoznao: {detail.payment.personName ?? "nije identifikovano"} · Pravilo: {
          matchRuleLabel(detail.bankTransaction?.rule ?? null)}</p>
        <p>Raspoređeno: {formatMoney(detail.allocated, detail.payment.currencyCode)} · Preostali kredit: {
          formatMoney(detail.credit, detail.payment.currencyCode)}</p>
        {!detail.payment.personId && allowed("Payments.ReconcileManual") && <div>
          <label>Identifikuj uplatioca<select value={manualPersonId} onChange={(event) => setManualPersonId(event.target.value)}>
            <option value="">Odaberi</option>{people.items.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
          <button type="button" disabled={!manualPersonId || busy} onClick={() => void mutate(`/${selectedId}/identify`,
            { personId: manualPersonId }, "Uplatilac je identifikovan.")}>POTVRDI IDENTIFIKACIJU</button></div>}
        {detail.payment.personId && allowed("Payments.ChangePayer") && <div>
          <button type="button" onClick={() => setChangePayerOpen((open) => !open)}>ISPRAVI UPLATIOCA</button>
          {changePayerOpen && <div className="admin-form">
            {activeAllocationCount(detail.allocations) > 0 && <p role="alert">Uplata je raspoređena na potraživanja trenutnog vlasnika.
              Za promjenu uplatioca postojeće raspodjele moraju biti poništene.</p>}
            <label>Novi uplatilac<select value={newPayerId} onChange={(event) => setNewPayerId(event.target.value)}>
              <option value="">Odaberi</option>{people.items.filter((item) => item.id !== detail.payment.personId)
                .map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
            <label>Razlog ispravke *<input value={payerReason} maxLength={500}
              onChange={(event) => setPayerReason(event.target.value)} /></label>
            {activeAllocations.length > 0 && <label><input type="checkbox" checked={payerWarningAccepted}
              onChange={(event) => setPayerWarningAccepted(event.target.checked)} /> Potvrđujem reverziju {
                activeAllocations.length} postojećih alokacija</label>}
            <button type="button" disabled={busy || !canSubmitPayerCorrection(detail.payment.personId,
              newPayerId, payerReason, activeAllocations.length, payerWarningAccepted)} onClick={() => {
                void mutate(`/${selectedId}/change-payer`, { personId: newPayerId, reason: payerReason,
                  allocations: [] }, "Uplatilac je ispravljen; stare alokacije su append-only reverzirane.")
                  .then((success) => { if (success) { setChangePayerOpen(false); setNewPayerId("");
                    setPayerReason(""); setPayerWarningAccepted(false); } });
              }}>POTVRDI ISPRAVKU</button>
          </div>}
        </div>}
        {detail.identificationHistory.length > 0 && <details><summary>Istorija identifikacije uplatioca</summary>
          <ul>{detail.identificationHistory.map((entry) => <li key={entry.id}>{new Date(entry.changedUtc)
            .toLocaleString("bs-BA")} · {entry.previousPersonName ?? "Neprepoznat"} → {entry.newPersonName}
            · {entry.reason} · {entry.actorName ?? "Sistem"}</li>)}</ul></details>}
        <h4>Detalji alokacija</h4><ul>{detail.allocations.map((item) => <li key={item.id}>
          {item.invoiceNumber ?? item.releaseInvoiceNumber ?? "Legacy"} / {item.unitCode} / {item.description ?? item.period}
          · {formatMoney(item.amount, detail.payment.currencyCode)} {item.reversesAllocationId ? "· OSLOBOĐEN KREDIT / ISPRAVKA" : ""}
          {item.financialDocumentId && item.releaseInvoiceNumber && ` · Kredit oslobođen stornom računa ${item.releaseInvoiceNumber}`}
          {activeAllocations.some((active) => active.id === item.id) && allowed("Payments.ReverseAllocation")
            && <button type="button" disabled={busy}
            onClick={() => { if (!reverseReason.trim()) { setError("Unesite razlog ispravke raspoređivanja."); return; }
              void mutate(`/allocations/${item.id}/reverse`, { reason: reverseReason }, "Alokacija je append-only reverzirana."); }}>
            ISPRAVI RASPOREĐIVANJE</button>}</li>)}</ul>
        {allowed("Payments.ReverseAllocation") && <label>Razlog ispravke<input value={reverseReason}
          onChange={(event) => setReverseReason(event.target.value)} maxLength={500} /></label>}
        {detail.credit > 0 && detail.payment.personId && allowed("Payments.ReconcileManual") && <><button type="button"
          onClick={() => void callApi<AutoPreview>(`/api/zevs/${zevId}/payments/${selectedId}/automatic-preview`, {}, token)
            .then(setAutoPreview).catch((problem) => setError(errorText(problem)))}>RASPOREDI AUTOMATSKI · preview</button>
          {autoPreview && <div><p>Raspoloživo: {formatMoney(autoPreview.available, detail.payment.currencyCode)}
            · Predloženo: {formatMoney(autoPreview.allocated, detail.payment.currencyCode)}</p>
            <ul>{autoPreview.lines.map((line) => <li key={line.chargeId}>{line.invoiceNumber ?? line.period}
              · dospijeće {line.dueDate} · {formatMoney(line.amount, detail.payment.currencyCode)}</li>)}</ul>
            <button type="button" disabled={busy || autoPreview.lines.length === 0}
              onClick={() => void mutate(`/${selectedId}/automatic`, { requestId: crypto.randomUUID() },
                "Kredit je automatski raspoređen.")}>POTVRDI</button></div>}</>}
      </>}
      {manualOpen && <><h3>Nova ručna uplata</h3><div className="admin-form">
        <label>Pretraži vlasnika<input value={personSearch} onChange={(event) => setPersonSearch(event.target.value)} /></label>
        <label>Vlasnik *<select value={manualPersonId} onChange={(event) => { setManualPersonId(event.target.value);
          setManualDraft({}); }}><option value="">Odaberite</option>{people.items.map((item) =>
            <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
        <label>Fond *<select value={manualFundId} onChange={(event) => { setManualFundId(event.target.value);
          setManualDraft({}); }}><option value="">Odaberite</option>{fundChoices.map((item) =>
            <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Jedinica (opciono)<select value={manualUnitId} onChange={(event) => setManualUnitId(event.target.value)}>
          <option value="">Nije vezana za jednu jedinicu</option>{unitOptions.map((item) =>
            <option key={item.id} value={item.id}>{item.code}</option>)}</select></label>
        <label>Datum *<input type="date" value={manualDate} onChange={(event) => setManualDate(event.target.value)} /></label>
        <label>Iznos *<input type="number" min="0.01" step="0.01" value={manualAmount}
          onChange={(event) => setManualAmount(event.target.value)} /></label>
        <label>Valuta *<input maxLength={3} value={manualCurrency}
          onChange={(event) => setManualCurrency(event.target.value.toUpperCase())} /></label>
        <label>Način plaćanja *<select value={manualMethod} onChange={(event) => setManualMethod(event.target.value)}>
          <option value="">Odaberite</option>{creditPolicy?.allowManualCash && <option value="Cash">Gotovina</option>}
          <option value="Migration">Migraciona uplata</option><option value="Other">Drugo</option></select></label>
        <label>Referenca<input value={manualReference} onChange={(event) => setManualReference(event.target.value)} /></label>
        <label>Opis<input value={manualDescription} onChange={(event) => setManualDescription(event.target.value)} /></label>
        <label>Razlog *<input maxLength={500} value={manualReason}
          onChange={(event) => setManualReason(event.target.value)} /></label></div>
        <button type="button" disabled={busy} onClick={() => void createManual(false)}>SAČUVAJ KAO KREDIT</button>
        <button type="button" disabled={busy || Object.keys(manualDraft).length === 0}
          onClick={() => void createManual(true)}>SAČUVAJ I RASPOREDI</button></>}
      {(detail?.payment.personId || manualOpen && manualPersonId) && <><h4>Otvorena potraživanja istog vlasnika i fonda</h4>
        <label>Pretraži vlasnika, jedinicu, račun ili poziv<input value={chargeSearch}
          onChange={(event) => { setChargeSearch(event.target.value); setChargePage(1);
            setDraft({}); setManualDraft({}); }} /></label>
        <div className="table-scroll"><table><thead><tr><th>Odaberi</th><th>Račun</th><th>Jedinica</th>
          <th>Period</th><th>Dospijeće</th><th>Poziv</th><th>Otvoreno</th><th>Rasporedi</th></tr></thead>
          <tbody>{charges.items.map((item) => {
            const values = manualOpen ? manualDraft : draft;
            const setValues = manualOpen ? setManualDraft : setDraft;
            return <tr key={item.id}><td><input type="checkbox" checked={values[item.id] !== undefined}
              onChange={(event) => setValues((current) => { const next = { ...current };
                if (event.target.checked) next[item.id] = String(Math.min(item.open,
                  manualOpen ? Number(manualAmount) || item.open : detail?.credit ?? item.open));
                else delete next[item.id]; return next; })} /></td><td>{item.invoiceNumber ?? "Legacy"}</td>
              <td>{item.unitCode}</td><td>{item.period}</td><td>{item.dueDate}</td><td>{item.reference}</td>
              <td>{item.open.toFixed(2)}</td><td><input type="number" min="0.01" max={item.open} step="0.01"
                disabled={values[item.id] === undefined} value={values[item.id] ?? ""}
                onChange={(event) => setValues((current) => ({ ...current, [item.id]: event.target.value }))} /></td></tr>;
          })}</tbody></table></div>
        {charges.items.length === 0 && <p className="empty-inline">Nema otvorenih stavki za odabrani fond.</p>}
        <Pager {...charges} onPage={(nextPage) => { setChargePage(nextPage); setDraft({}); setManualDraft({}); }} />
        {detail && allowed("Payments.ReconcileManual") && <><p>Predloženo ručno raspoređivanje: {
          formatMoney(allocatedDraft, detail.payment.currencyCode)}</p>
          <button type="button" disabled={busy || allocatedDraft <= 0 || allocatedDraft > detail.credit}
            onClick={() => void mutate(`/${selectedId}/allocations`,
              { requestId: crypto.randomUUID(), allocations: selectedLines(draft) },
              "Uplata je raspoređena na odabrana potraživanja.")}>POTVRDI RASPOREĐIVANJE / ISKORISTI KREDIT</button></>}
      </>}
    </div>}
  </section>;
}
