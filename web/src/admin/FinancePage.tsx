import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { ApiError, callApi, errorText, Page } from "../api";
import { Lookup, Pager, SectionTitle } from "./shared";
import InvoiceDocuments from "./InvoiceDocuments.tsx";
import { useChangeRevision } from "../useChangeRevision.ts";
import "../finance.css";
import { billingBasisLabel, billingDate, billingDraftSignature, billingIssueText, billingItemTitle, billingMoney,
  defaultBillingSelection, invoiceListView, similarBillingWarnings } from "../billingUi.ts";
import { manualLineAmount } from "../billingUi.ts";
import { formatMoney, ownerLedgerLabel } from "../currency.ts";
import type { BillingAdjustment, BillingIssue, BillingItem, BillingManualLine } from "../billingUi.ts";

type Fund = { id: string; kind: string; name: string; balance: number | null;
  currencyCode: string | null; balancesByCurrency: { currencyCode: string; balance: number }[];
  requiresBankAccountForInvoice: boolean };
type Tariff = BillingItem & { description: string | null; displayOrder: number; rowVersion: string };
type Charge = { id: string; personName: string; period: string; amount: number; dueDate: string;
  reference: string; currencyCode: string };
type ZevOption = { id: string; code: string; name: string };
type BillingPreview = { fingerprint: string; creditPlanHash: string | null; currency: string;
  totalAmount: number; chargeCount: number;
  autoCreditEnabled: boolean; proposedCreditTotal: number; remainingToPay: number;
  units: { propertyUnitId: string; unitCode: string; personName: string; areaSquareMeters: number;
    registeredMembers: number; total: number; availableCredit: number; proposedCredit: number;
    remainingToPay: number; lines: { tariffVersionId: string | null; code: string; name: string;
      basis: string; fundId: string; fundName: string; quantity: number; rate: number;
      ownershipShare: number; amount: number;
      isManualOverride: boolean; grossAmount: number; discountType: string;
      discountValue: number; discountAmount: number; unitOfMeasure: string | null }[] }[] };
type BillingPreflight = { canPreview: boolean; issues: BillingIssue[] };
type Invoice = { id: string | null; propertyUnitId: string; personId: string; unitCode: string;
  personName: string; total: number; effectiveTotal: number | null; paid: number; open: number;
  currencyCode: string; invoiceNumber: string | null;
  issueDate: string | null; dueDate: string | null; paymentStatus: string; documentStatus: string };
type InvoiceDetail = { propertyUnitId: string; personId: string; currentTotal: number;
  currency: string;
  previousOpen: number; totalOpen: number; paymentReference: string; invoiceNumber: string | null;
  issueDate: string | null; dueDate: string | null; totalDueAtIssue: number; discountTotal: number;
  currentPaid: number; paymentStatus: string; documentStatus: string; personName: string;
  credit: number; paidBeforeStorno: number; stornoCredit: number; effectiveTotal: number | null;
  lines: { id: string | null; fundId: string; code: string; name: string;
    unitOfMeasure: string | null; quantity: number | null;
    unitPrice: number | null; amountBeforeDiscount: number | null; discountAmount: number | null;
    amount: number; note: string | null }[];
  payments: { paymentId: string; paidOn: string; amount: number }[];
  accounts: { fundId: string; fundName: string; accountNumber: string;
    currencyCode: string; currentAmount: number }[] };
type Ledger = { opening: number | null; balance: number | null; page: number; pageSize: number;
  totalCount: number;
  balancesByCurrency: { currencyCode: string; charges: number; paid: number;
    open: number; balance: number }[];
  openingByCurrency: { currencyCode: string; balance: number }[];
  items: { date: string; kind: string; description: string; document: string;
    currencyCode: string; signedAmount: number; balance: number }[] };
type OwnerCard = { balances: { currencyCode: string; totalCharges: number; totalPayments: number;
  open: number; credit: number; netBalance: number }[]; page: number; pageSize: number;
  totalCount: number; events: { date: string; kind: string; document: string;
    description: string; currencyCode: string; debt: number; payment: number;
    creditDelta: number; runningBalance: number }[] };

export default function FinancePage({ token, zevs, permissions }: { token: string; zevs: ZevOption[];
  permissions?: string[] }) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [funds, setFunds] = useState<Fund[]>([]);
  const [tariffs, setTariffs] = useState<Page<Tariff>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [billingItems, setBillingItems] = useState<BillingItem[]>([]);
  const [billingItemsLoading, setBillingItemsLoading] = useState(false);
  const [charges, setCharges] = useState<Page<Charge>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [tariffPage, setTariffPage] = useState(1);
  const [chargePage, setChargePage] = useState(1);
  const [fundKind, setFundKind] = useState("Regular");
  const [fundName, setFundName] = useState("");
  const [fundRequiresBankAccount, setFundRequiresBankAccount] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [basis, setBasis] = useState("SquareMeter");
  const [rate, setRate] = useState("");
  const [fundId, setFundId] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [validTo, setValidTo] = useState("");
  const [description, setDescription] = useState("");
  const [isOptional, setIsOptional] = useState(false);
  const [isActive, setIsActive] = useState(true);
  const [showOnInvoice, setShowOnInvoice] = useState(true);
  const [displayOrder, setDisplayOrder] = useState("0");
  const [period, setPeriod] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [selectedTariffIds, setSelectedTariffIds] = useState<string[]>([]);
  const [adjustments, setAdjustments] = useState<BillingAdjustment[]>([]);
  const [manualLines, setManualLines] = useState<BillingManualLine[]>([]);
  const [manualUnitId, setManualUnitId] = useState("");
  const [manualFundId, setManualFundId] = useState("");
  const [manualDescription, setManualDescription] = useState("");
  const [manualMeasure, setManualMeasure] = useState("kom");
  const [manualQuantity, setManualQuantity] = useState("1");
  const [manualPrice, setManualPrice] = useState("");
  const [manualDiscountType, setManualDiscountType] = useState("None");
  const [manualDiscountValue, setManualDiscountValue] = useState("");
  const [manualNote, setManualNote] = useState("");
  const [manualReason, setManualReason] = useState("");
  const [preview, setPreview] = useState<BillingPreview | null>(null);
  const [previewSignature, setPreviewSignature] = useState("");
  const [preflightIssues, setPreflightIssues] = useState<BillingIssue[]>([]);
  const [adjustUnitId, setAdjustUnitId] = useState("");
  const [adjustTariffId, setAdjustTariffId] = useState("");
  const [adjustAmount, setAdjustAmount] = useState("");
  const [adjustInclude, setAdjustInclude] = useState(true);
  const [adjustReason, setAdjustReason] = useState("");
  const [adjustNote, setAdjustNote] = useState("");
  const [adjustDescription, setAdjustDescription] = useState("");
  const [adjustUnitOfMeasure, setAdjustUnitOfMeasure] = useState("");
  const [adjustQuantity, setAdjustQuantity] = useState("");
  const [adjustUnitPrice, setAdjustUnitPrice] = useState("");
  const [adjustDiscountType, setAdjustDiscountType] = useState("None");
  const [adjustDiscountValue, setAdjustDiscountValue] = useState("");
  const [invoices, setInvoices] = useState<Page<Invoice>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [invoicePage, setInvoicePage] = useState(1);
  const [ledger, setLedger] = useState<Ledger | null>(null);
  const [ledgerPage, setLedgerPage] = useState(1);
  const [ledgerFrom, setLedgerFrom] = useState("");
  const [ledgerTo, setLedgerTo] = useState("");
  const [ownerCard, setOwnerCard] = useState<OwnerCard | null>(null);
  const [ownerCardPersonId, setOwnerCardPersonId] = useState("");
  const [ownerCardPage, setOwnerCardPage] = useState(1);
  const [ownerCardFrom, setOwnerCardFrom] = useState("");
  const [ownerCardTo, setOwnerCardTo] = useState("");
  const [invoiceDetail, setInvoiceDetail] = useState<InvoiceDetail | null>(null);
  const [selectedInvoice, setSelectedInvoice] = useState<Invoice | null>(null);
  const [ledgerKey, setLedgerKey] = useState<{ unitId: string; personId: string } | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const selectionScope = useRef("");
  const revision = useChangeRevision(`zev:${zevId}:finance`, `zev:${zevId}:ownerships`,
    `zev:${zevId}:units`, "ownerships", "propertyUnits");
  const currentSignature = `${revision}:${billingDraftSignature(zevId, period, dueDate, selectedTariffIds, adjustments, manualLines)}`;
  const currentPreview = previewSignature === currentSignature ? preview : null;
  const signatureRef = useRef(currentSignature);
  signatureRef.current = currentSignature;

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const prefix = `/api/zevs/${zevId}/finance`;
      const [fundData, tariffData, chargeData] = await Promise.all([
        allowed("Funds.View") ? callApi<Fund[]>(`${prefix}/funds`, {}, token) : Promise.resolve([]),
        callApi<Page<Tariff>>(`${prefix}/tariffs?page=${tariffPage}&pageSize=100`, {}, token),
        allowed("Billing.View") ? callApi<Page<Charge>>(`${prefix}/charges?page=${chargePage}`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
      ]);
      if (currentRequest !== requestId.current) return;
      if (tariffPage > 1 && tariffData.items.length === 0) { setTariffPage(Math.max(1, Math.ceil(tariffData.totalCount / 100))); return; }
      if (chargePage > 1 && chargeData.items.length === 0) { setChargePage(Math.max(1, Math.ceil(chargeData.totalCount / 20))); return; }
      setFunds(fundData); setTariffs(tariffData); setCharges(chargeData); setError("");
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, tariffPage, chargePage, token, permissions]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);
  useEffect(() => { setPreview(null); setPreflightIssues([]); setError(""); }, [revision]);
  useEffect(() => {
    if (!period || !zevId) { setBillingItems([]); setBillingItemsLoading(false); return; }
    let active = true;
    setBillingItemsLoading(true);
    void callApi<BillingItem[]>(`/api/zevs/${zevId}/finance/billing-items?period=${period}-01`, {}, token)
      .then((items) => {
        if (!active) return;
        setBillingItems(items);
        const scope = `${zevId}:${period}`;
        if (selectionScope.current !== scope) {
          selectionScope.current = scope;
          setSelectedTariffIds(defaultBillingSelection(items));
        } else {
          const available = new Set(items.map((item) => item.id));
          setSelectedTariffIds((current) => current.filter((id) => available.has(id)));
        }
      })
      .catch((problem) => { if (active) setError(errorText(problem)); })
      .finally(() => { if (active) setBillingItemsLoading(false); });
    return () => { active = false; };
  }, [zevId, period, revision, token]);
  useEffect(() => {
    if (!period || !zevId) return;
    let active = true;
    void callApi<Page<Invoice>>(`/api/zevs/${zevId}/finance/invoices?period=${period}-01&page=${invoicePage}`,
      {}, token).then((result) => { if (active) setInvoices(result); })
      .catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [zevId, period, invoicePage, revision, token]);
  useEffect(() => {
    if (!ledgerKey || !zevId) return;
    let active = true;
    const parameters = new URLSearchParams({ personId: ledgerKey.personId,
      page: String(ledgerPage), pageSize: "20" });
    if (ledgerFrom) parameters.set("from", ledgerFrom);
    if (ledgerTo) parameters.set("to", ledgerTo);
    void callApi<Ledger>(`/api/zevs/${zevId}/payments/unit-ledger/${ledgerKey.unitId}?${parameters}`,
      {}, token).then((result) => { if (active) setLedger(result); })
      .catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [ledgerKey, ledgerPage, ledgerFrom, ledgerTo, zevId, revision, token]);
  useEffect(() => {
    if (!ownerCardPersonId || !zevId) { setOwnerCard(null); return; }
    let active = true;
    const parameters = new URLSearchParams({ page: String(ownerCardPage), pageSize: "20" });
    if (ownerCardFrom) parameters.set("from", ownerCardFrom);
    if (ownerCardTo) parameters.set("to", ownerCardTo);
    void callApi<OwnerCard>(`/api/zevs/${zevId}/payments/owner-card/${ownerCardPersonId}`
      + `?${parameters}`, {}, token)
      .then((result) => { if (active) setOwnerCard(result); })
      .catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [ownerCardPersonId, ownerCardPage, ownerCardFrom, ownerCardTo, zevId, revision, token]);
  useEffect(() => {
    if (!selectedInvoice || !period || !zevId) return;
    let active = true;
    void callApi<InvoiceDetail>(`/api/zevs/${zevId}/finance/invoices/${selectedInvoice.propertyUnitId}/${selectedInvoice.personId}?period=${period}-01`,
      {}, token).then((result) => { if (active) setInvoiceDetail(result); })
      .catch((problem) => { if (active) setError(errorText(problem)); });
    return () => { active = false; };
  }, [selectedInvoice, period, zevId, revision, token]);

  async function submit(path: string, body: object, success: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/finance/${path}`, { method: "POST", body: JSON.stringify(body) }, token);
      setNotice(success);
      if (path === "funds") setFundName("");
      if (path === "tariffs") { setCode(""); setName(""); setRate(""); setValidFrom(""); setPreview(null); }
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function addFund(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("funds", { kind: fundKind, name: fundName,
      requiresBankAccountForInvoice: fundRequiresBankAccount }, "Fond je sačuvan.");
  }

  function addTariff(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("tariffs", { code, name, basis, rate: Number(rate), fundId, validFrom,
      validTo: validTo || null, description: description || null, isActive, isOptional, showOnInvoice,
      displayOrder: Number(displayOrder) }, "Nova verzija stavke je sačuvana.");
  }

  const activeItems = billingItems;
  const similarWarnings = similarBillingWarnings(activeItems, selectedTariffIds);

  function draft(expectedHash: string | null = null, expectedCreditPlanHash: string | null = null) {
    return { tariffVersionIds: selectedTariffIds, adjustments, manualLines, expectedHash, expectedCreditPlanHash };
  }

  async function previewBilling(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return;
    const requestedSignature = currentSignature;
    pending.current = true; setBusy(true); setError(""); setNotice(""); setPreview(null); setPreflightIssues([]);
    try {
      const requestBody = { period: `${period}-01`, dueDate, draft: draft() };
      const preflight = await callApi<BillingPreflight>(`/api/zevs/${zevId}/finance/billing-runs/preflight`,
        { method: "POST", body: JSON.stringify(requestBody) }, token);
      if (signatureRef.current !== requestedSignature) return;
      if (!preflight.canPreview) { setPreflightIssues(preflight.issues); return; }
      const result = await callApi<BillingPreview>(`/api/zevs/${zevId}/finance/billing-runs/preview`,
        { method: "POST", body: JSON.stringify(requestBody) }, token);
      if (signatureRef.current === requestedSignature) { setPreview(result); setPreviewSignature(requestedSignature); }
    } catch (problem) {
      if (signatureRef.current !== requestedSignature) return;
      if (problem instanceof ApiError && problem.issues.length) setPreflightIssues(problem.issues);
      else setError(errorText(problem));
    }
    finally { pending.current = false; setBusy(false); }
  }

  async function postBilling() {
    if (pending.current || !currentPreview) return;
    if (currentPreview.autoCreditEnabled && !currentPreview.creditPlanHash) {
      setError("Plan primjene kredita nije potvrđen. Ponovite pregled obračuna."); return;
    }
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const result = await callApi<{ alreadyExists: boolean }>(`/api/zevs/${zevId}/finance/billing-runs`,
        { method: "POST", body: JSON.stringify({ period: `${period}-01`, dueDate,
          draft: draft(currentPreview.fingerprint, currentPreview.creditPlanHash) }) }, token);
      setNotice(result.alreadyExists ? "Obračun za taj period je već knjižen." : "Obračun je knjižen.");
      setPreview(null);
      await loadInvoices();
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function addAdjustment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!adjustUnitId || !adjustTariffId) return;
    const adjustment: BillingAdjustment = { propertyUnitId: adjustUnitId, tariffVersionId: adjustTariffId,
      include: adjustInclude, manualAmount: adjustInclude && adjustAmount ? Number(adjustAmount) : null,
      note: adjustNote || null, reason: adjustReason || null,
      description: adjustDescription || null, unitOfMeasure: adjustUnitOfMeasure || null,
      quantity: adjustQuantity ? Number(adjustQuantity) : null, unitPrice: adjustUnitPrice ? Number(adjustUnitPrice) : null,
      discountType: adjustDiscountType, discountValue: adjustDiscountValue ? Number(adjustDiscountValue) : null };
    setAdjustments((current) => [...current.filter((item) => item.propertyUnitId !== adjustUnitId
      || item.tariffVersionId !== adjustTariffId), adjustment]);
    setPreview(null); setPreflightIssues([]); setAdjustAmount(""); setAdjustNote(""); setAdjustReason("");
    setAdjustDescription(""); setAdjustUnitOfMeasure(""); setAdjustQuantity("");
    setAdjustUnitPrice(""); setAdjustDiscountType("None"); setAdjustDiscountValue("");
  }

  function addManualLine(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!manualUnitId || !manualFundId || !manualDescription.trim() || !manualReason.trim()) return;
    const line: BillingManualLine = { id: crypto.randomUUID(), propertyUnitId: manualUnitId,
      fundId: manualFundId, description: manualDescription.trim(), unitOfMeasure: manualMeasure.trim(),
      quantity: Number(manualQuantity), unitPrice: Number(manualPrice), discountType: manualDiscountType,
      discountValue: manualDiscountType === "None" ? null : Number(manualDiscountValue),
      note: manualNote.trim() || null, reason: manualReason.trim() };
    if (manualLineAmount(line) < 0) { setError("Popust ne može biti veći od iznosa stavke."); return; }
    setManualLines((current) => [...current, line]); setPreview(null); setPreflightIssues([]); setError("");
    setManualDescription(""); setManualPrice(""); setManualDiscountType("None");
    setManualDiscountValue(""); setManualNote(""); setManualReason("");
  }

  async function loadInvoices() {
    if (!period || !zevId) return;
    try {
      setInvoices(await callApi<Page<Invoice>>(`/api/zevs/${zevId}/finance/invoices?period=${period}-01&page=${invoicePage}`,
        {}, token));
    } catch (problem) { setError(errorText(problem)); }
  }

  async function downloadInvoice(invoice: Invoice) {
    if (pending.current) return;
    pending.current = true; setBusy(true);
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/finance/invoices/${invoice.propertyUnitId}/${invoice.personId}.pdf?period=${period}-01`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("PDF račun nije dostupan.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = `racun-${invoice.unitCode}-${period}.pdf`;
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function loadLedger(invoice: Invoice) {
    setLedgerPage(1);
    setLedgerKey({ unitId: invoice.propertyUnitId, personId: invoice.personId });
  }

  async function downloadSlip(invoice: Invoice, fundId: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true);
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/finance/invoices/${invoice.propertyUnitId}/${invoice.personId}/payment-slip.pdf?period=${period}-01&fundId=${fundId}`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Uplatnica nije dostupna za otvoreni iznos fonda.");
      const url = URL.createObjectURL(await response.blob());
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = `uplatnica-${invoice.unitCode}-${period}.pdf`;
      anchor.click(); URL.revokeObjectURL(url);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  async function toggleTariff(item: Tariff) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError("");
    try {
      await callApi(`/api/zevs/${zevId}/finance/tariffs/${item.id}/status`, {
        method: "PATCH", body: JSON.stringify({ isActive: !item.isActive, rowVersion: item.rowVersion }),
      }, token);
      setNotice(item.isActive ? "Stavka je deaktivirana." : "Stavka je aktivirana."); setPreview(null);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  const showUatBadge = import.meta.env.DEV || import.meta.env.VITE_APP_ENV === "UAT";

  return <>
    <SectionTitle title="Finansije" description="Stavke računa, pregled obračuna, knjiženje i računi." />
    <div className="panel finance-selector"><label>ZEV <select value={zevId} onChange={(event) => { setZevId(event.target.value); setPreview(null); setPreflightIssues([]); setLedger(null); setLedgerKey(null); setInvoiceDetail(null); setSelectedInvoice(null); setSelectedTariffIds([]); }}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}
    {notice && <p className="notice" role="status">{notice}</p>}
    {!zevId ? <p className="empty-inline">Nema dostupnih ZEV-ova.</p> : <>
      <div className="finance-grid">
        <section className="panel"><h2>Fondovi</h2>
          {funds.map((fund) => <div className="fund-row" key={fund.id}><span>{fund.name} ({fund.kind}) ·
            {fund.requiresBankAccountForInvoice ? " Račun obavezan" : " Račun nije obavezan"}</span>
            <strong>{fund.balancesByCurrency.length ? fund.balancesByCurrency.map((balance) =>
              formatMoney(balance.balance, balance.currencyCode)).join(" · ") : "Nema transakcija"}</strong>
            {allowed("Funds.Edit") && <button type="button" disabled={busy} onClick={() => {
              void callApi(`/api/zevs/${zevId}/finance/funds/${fund.id}/bank-account-requirement`,
                { method: "PUT", body: JSON.stringify({ requiresBankAccountForInvoice: !fund.requiresBankAccountForInvoice }) }, token)
                .then(() => { setPreview(null); void refresh(); }).catch((problem) => setError(errorText(problem)));
            }}>Promijeni zahtjev računa</button>}</div>)}
          {allowed("Funds.Create") && <form className="admin-form" onSubmit={addFund}>
            <label>Vrsta<select value={fundKind} onChange={(event) => setFundKind(event.target.value)}>
              <option value="Regular">Redovni</option><option value="Reserve">Rezervni</option></select></label>
            <label>Naziv<input required maxLength={120} value={fundName} onChange={(event) => setFundName(event.target.value)} /></label>
            <label><input type="checkbox" checked={fundRequiresBankAccount}
              onChange={(event) => setFundRequiresBankAccount(event.target.checked)} /> Zahtijevaj aktivan bankovni račun prije knjiženja</label>
            <button type="submit" disabled={busy}>Dodaj fond</button>
          </form>}</section>
        <section className="panel"><h2>Stavke računa · nova verzija</h2>
          {allowed("Finance.Create") && <form className="admin-form" onSubmit={addTariff}>
            <label>Šifra<input required maxLength={50} value={code} onChange={(event) => setCode(event.target.value)} /></label>
            <label>Naziv<input required maxLength={200} value={name} onChange={(event) => setName(event.target.value)} /></label>
            <label>Opis<textarea maxLength={1000} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
            <label>Osnova<select value={basis} onChange={(event) => setBasis(event.target.value)}>
              <option value="SquareMeter">Po m²</option><option value="FixedUnit">Fiksno po jedinici</option>
              <option value="RegisteredMember">Po članu</option><option value="ManualAmount">Ručni iznos</option>
              <option value="OneTime">Jednokratno</option><option value="Extraordinary">Vanredno (pojedinačno)</option></select></label>
            <label>Vrijednost<input required type="number" min="0" step="0.0001" value={rate} onChange={(event) => setRate(event.target.value)} /></label>
            <label>Fond<select required value={fundId} onChange={(event) => setFundId(event.target.value)}>
              <option value="">Odaberite</option>{funds.map((fund) => <option key={fund.id} value={fund.id}>{fund.name}</option>)}</select></label>
            <label>Vrijedi od<input required type="date" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></label>
            <label>Vrijedi do<input type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} /></label>
            <label>Redoslijed<input type="number" min="0" value={displayOrder} onChange={(event) => setDisplayOrder(event.target.value)} /></label>
            <label><input type="checkbox" checked={isOptional} onChange={(event) => setIsOptional(event.target.checked)} /> Neobavezna stavka</label>
            <label><input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} /> Aktivna</label>
            <label><input type="checkbox" checked={showOnInvoice} onChange={(event) => setShowOnInvoice(event.target.checked)} /> Prikaži na računu</label>
            <button type="submit" disabled={busy}>Sačuvaj stavku</button>
          </form>}</section>
        <section className="panel"><h2>Mjesečni obračun</h2>
          <form className="admin-form" onSubmit={previewBilling}>
            <label>Period<input required type="month" value={period} onChange={(event) => {
              const next = event.target.value; setPeriod(next); setPreview(null); setAdjustments([]); setManualLines([]);
              setSelectedInvoice(null); setInvoiceDetail(null);
              setSelectedTariffIds([]); setBillingItems([]); setPreflightIssues([]);
            }} /></label>
            <label>Rok dospijeća<input required type="date" value={dueDate} onChange={(event) => { setDueDate(event.target.value); setPreview(null); setPreflightIssues([]); }} /></label>
            <fieldset><legend>Stavke za obračun</legend>
              {billingItemsLoading && <p className="empty-inline">Učitavanje stavki…</p>}
              {!billingItemsLoading && activeItems.length === 0 && <p className="empty-inline">Nema važećih stavki za period.</p>}
              {activeItems.filter((item) => item.basis !== "Extraordinary" && item.basis !== "ManualAmount")
                .map((item) => <label className="billing-item-label" key={item.id}>
                  <input type="checkbox" checked={selectedTariffIds.includes(item.id)}
                    onChange={(event) => { setPreview(null); setPreflightIssues([]); setSelectedTariffIds((current) => event.target.checked
                      ? [...current, item.id] : current.filter((id) => id !== item.id)); }} />
                  <span><strong>{showUatBadge && item.code.startsWith("UAT-") && <span className="uat-badge">UAT</span>}
                    {billingItemTitle(item)}</strong>
                    <small>{billingBasisLabel(item.basis)} · {billingMoney(item.rate)} · Fond: {item.fundName}
                      {item.fundIsActive === false ? " (neaktivan)" : ""}</small>
                    <small>Verzija {item.version} · od {billingDate(item.validFrom)} · do {billingDate(item.validTo)} ·
                      {item.isOptional ? " Opcionalna" : " Obavezna"}</small></span>
                </label>)}
            </fieldset>
            <p>Odabrano stavki: {selectedTariffIds.length}. Opcionalne stavke nisu automatski uključene.</p>
            {similarWarnings.map((warning) => <p className="billing-warning" role="status" key={warning}>{warning}</p>)}
            {preflightIssues.length > 0 && <div className="billing-issues" role="alert">
              <strong>OBRAČUN NIJE SPREMAN ZA KNJIŽENJE</strong>
              <ul>{preflightIssues.map((issue, index) => <li key={`${issue.code}-${issue.propertyUnitCode ?? issue.tariffCode ?? index}`}>
                {billingIssueText(issue, period)}</li>)}</ul>
            </div>}
            {allowed("Billing.Preview") && <button type="submit" disabled={busy || !period || billingItemsLoading}>
              {busy ? "Provjera u toku..." : "Pregled obračuna"}</button>}
          </form></section>
      </div>
      {period && allowed("Billing.Preview") && <section className="panel"><form className="admin-form" onSubmit={addAdjustment}><h2>Ručna dorada prije knjiženja</h2>
        <Lookup token={token} label="Stan" path={`/api/zevs/${zevId}/finance/units`}
          value={adjustUnitId} onChange={(value) => { setAdjustUnitId(value); setPreview(null); }} required />
        <label>Stavka<select required value={adjustTariffId} onChange={(event) => { setAdjustTariffId(event.target.value); setPreview(null); }}>
          <option value="">Odaberite</option>{activeItems.map((item) => <option key={item.id} value={item.id}>
            {item.code} | {item.name} · {billingBasisLabel(item.basis)} · {billingMoney(item.rate)}</option>)}</select></label>
        <label><input type="checkbox" checked={adjustInclude} onChange={(event) => { setAdjustInclude(event.target.checked); setPreview(null); }} /> Uključi stavku za ovaj stan (isključiti je moguće samo ako je neobavezna)</label>
        <label>Ručni iznos (prazno = izračunata vrijednost)<input type="number" min="0" step="0.01" value={adjustAmount}
          onChange={(event) => { setAdjustAmount(event.target.value); setPreview(null); }} /></label>
        <label>Opis na računu<input maxLength={200} value={adjustDescription} onChange={(event) => setAdjustDescription(event.target.value)} /></label>
        <label>Jedinica mjere<input maxLength={30} value={adjustUnitOfMeasure} onChange={(event) => setAdjustUnitOfMeasure(event.target.value)} /></label>
        <label>Količina<input type="number" min="0.001" step="0.001" value={adjustQuantity} onChange={(event) => setAdjustQuantity(event.target.value)} /></label>
        <label>Jedinična cijena<input type="number" min="0" step="0.0001" value={adjustUnitPrice} onChange={(event) => setAdjustUnitPrice(event.target.value)} /></label>
        <label>Popust<select value={adjustDiscountType} onChange={(event) => setAdjustDiscountType(event.target.value)}>
          <option value="None">Bez popusta</option><option value="Percentage">Procenat</option>
          <option value="Fixed">Fiksni iznos</option></select></label>
        {adjustDiscountType !== "None" && <label>Vrijednost popusta<input type="number" min="0" step="0.01"
          max={adjustDiscountType === "Percentage" ? 100 : undefined} required value={adjustDiscountValue}
          onChange={(event) => setAdjustDiscountValue(event.target.value)} /></label>}
        <label>Razlog ručne dorade<input maxLength={500} value={adjustReason} onChange={(event) => setAdjustReason(event.target.value)} /></label>
        <label>Napomena<input maxLength={1000} value={adjustNote} onChange={(event) => { setAdjustNote(event.target.value); setPreview(null); }} /></label>
        <button type="submit" disabled={busy}>Sačuvaj draft izmjenu</button></form></section>}
      {period && allowed("Billing.Preview") && <section className="panel"><form className="admin-form" onSubmit={addManualLine}>
        <h2>Slobodna ručna stavka</h2><p>Bez šablona tarife; stavka se knjiži tek nakon pregleda i potvrde obračuna.</p>
        <Lookup token={token} label="Stan" path={`/api/zevs/${zevId}/finance/units`}
          value={manualUnitId} onChange={setManualUnitId} required />
        <label>Fond<select required value={manualFundId} onChange={(event) => setManualFundId(event.target.value)}>
          <option value="">Odaberite</option>{funds.map((fund) =>
            <option key={fund.id} value={fund.id}>{fund.name}</option>)}</select></label>
        <label>Opis<input required maxLength={200} value={manualDescription}
          onChange={(event) => setManualDescription(event.target.value)} /></label>
        <label>Jedinica mjere<input required maxLength={30} value={manualMeasure}
          onChange={(event) => setManualMeasure(event.target.value)} /></label>
        <label>Količina<input required type="number" min="0.001" step="0.001" value={manualQuantity}
          onChange={(event) => setManualQuantity(event.target.value)} /></label>
        <label>Jedinična cijena<input required type="number" min="0" step="0.0001" value={manualPrice}
          onChange={(event) => setManualPrice(event.target.value)} /></label>
        <label>Popust<select value={manualDiscountType} onChange={(event) => setManualDiscountType(event.target.value)}>
          <option value="None">Bez popusta</option><option value="Percentage">Procenat</option>
          <option value="Fixed">Fiksni iznos</option></select></label>
        {manualDiscountType !== "None" && <label>Vrijednost popusta<input required type="number" min="0.01"
          max={manualDiscountType === "Percentage" ? 100 : undefined} step="0.01"
          value={manualDiscountValue} onChange={(event) => setManualDiscountValue(event.target.value)} /></label>}
        <label>Razlog<input required maxLength={500} value={manualReason}
          onChange={(event) => setManualReason(event.target.value)} /></label>
        <label>Napomena<input maxLength={1000} value={manualNote}
          onChange={(event) => setManualNote(event.target.value)} /></label>
        <button type="submit" disabled={busy}>+ DODAJ RUČNU STAVKU</button>
      </form></section>}
      {manualLines.length > 0 && <section className="panel"><h2>Slobodne stavke u draftu</h2>
        {manualLines.map((line) => <div className="fund-row" key={line.id}><span>{line.description} ·
          {line.quantity} {line.unitOfMeasure} × {billingMoney(line.unitPrice)} = {billingMoney(manualLineAmount(line))}</span>
          <button type="button" onClick={() => { setManualLines((current) => current.filter((item) => item.id !== line.id));
            setPreview(null); setPreflightIssues([]); }}>Ukloni</button></div>)}</section>}
      {currentPreview && <section className="panel table-panel"><h2>Pregled prije knjiženja · {
        formatMoney(currentPreview.totalAmount, currentPreview.currency)}</h2>
        <p>Pregled nije knjižen. Svaka izmjena stavki zahtijeva novi pregled.</p>
        <p className="billing-summary">Stanova: {new Set(currentPreview.units.map((unit) => unit.propertyUnitId)).size} ·
          Zaduženja: {currentPreview.chargeCount} · Stavki: {new Set(currentPreview.units.flatMap((unit) =>
            unit.lines.map((line) => line.tariffVersionId ?? line.code))).size} ·
          Ukupno: {formatMoney(currentPreview.totalAmount, currentPreview.currency)}</p>
        {currentPreview.autoCreditEnabled && <p className="billing-summary">Novo zaduženje: {
          formatMoney(currentPreview.totalAmount, currentPreview.currency)} ·
          Predložena primjena kredita: −{formatMoney(currentPreview.proposedCreditTotal, currentPreview.currency)} ·
          Preostalo za uplatu: {formatMoney(currentPreview.remainingToPay, currentPreview.currency)}.
          Originalni PDF zadržava iznos izdatog računa; kredit se knjiži kao alokacija nakon izdavanja.</p>}
        <div className="table-scroll"><table><thead><tr><th>Stan</th><th>Vlasnik</th><th>m²</th><th>Članovi</th><th>Stavke obračuna</th><th>Ukupno</th></tr></thead>
          <tbody>{currentPreview.units.map((unit) => <tr key={`${unit.propertyUnitId}-${unit.personName}`}><td>{unit.unitCode}</td>
            <td>{unit.personName}</td><td>{unit.areaSquareMeters}</td><td>{unit.registeredMembers}</td>
            <td className="billing-line-cell"><ul className="billing-lines">{unit.lines.map((line) => <li key={line.tariffVersionId}>
              <strong>{line.code} | {line.name}</strong>
              <span>{line.quantity} {line.unitOfMeasure ?? "jedinica"} × {
                formatMoney(line.rate, currentPreview.currency)}
                {line.ownershipShare !== 1 ? ` × udio ${line.ownershipShare}` : ""}
                {line.discountAmount > 0 ? ` − popust ${formatMoney(line.discountAmount, currentPreview.currency)}` : ""}
                {" = "}{formatMoney(line.amount, currentPreview.currency)}</span>
              <small>{billingBasisLabel(line.basis)} · Fond: {line.fundName}</small>
            </li>)}</ul></td>
            <td>{formatMoney(unit.total, currentPreview.currency)}{currentPreview.autoCreditEnabled && <small>
              <br />Raspoloživi kredit: {formatMoney(unit.availableCredit, currentPreview.currency)}
              <br />Predložena primjena: −{formatMoney(unit.proposedCredit, currentPreview.currency)}
              <br />Preostalo: {formatMoney(unit.remainingToPay, currentPreview.currency)}</small>}</td></tr>)}</tbody></table></div>
        {allowed("Billing.Post") && <button type="button" disabled={busy} onClick={() => void postBilling()}>{busy ? "Knjiženje u toku..." : "Knjiži obračun"}</button>}
      </section>}
      {adjustments.length > 0 && <section className="panel"><h2>Draft izmjene</h2>{adjustments.map((item) =>
        <div className="fund-row" key={`${item.propertyUnitId}-${item.tariffVersionId}`}><span>{item.propertyUnitId} · {item.tariffVersionId} · {item.manualAmount ?? "automatski"}</span>
          <button type="button" onClick={() => { setAdjustments((current) => current.filter((entry) => entry !== item)); setPreview(null); setPreflightIssues([]); }}>Ukloni</button></div>)}</section>}
      <section className="panel table-panel"><h2>Verzije stavki računa</h2>
        <div className="table-scroll"><table><thead><tr><th>Šifra</th><th>Verzija</th><th>Naziv</th><th>Fond</th><th>Osnova</th><th>Vrijednost</th><th>Vrijedi od</th><th>Vrijedi do</th><th>Vrsta</th><th>Na računu</th><th>Status</th></tr></thead>
          <tbody>{tariffs.items.map((item) => <tr key={item.id}><td>{item.code}</td><td>{item.version}</td>
            <td>{item.name}</td><td>{item.fundName}</td><td>{billingBasisLabel(item.basis)}</td>
            <td>{billingMoney(item.rate)}</td><td>{billingDate(item.validFrom)}</td><td>{billingDate(item.validTo)}</td>
            <td>{item.isOptional ? "Opcionalna" : "Obavezna"}</td><td>{item.showOnInvoice ? "Da" : "Ne"}</td>
            <td>{item.isActive ? "Aktivna" : "Neaktivna"} {allowed("Finance.Edit") && <button type="button" disabled={busy}
              onClick={() => void toggleTariff(item)}>{item.isActive ? "Deaktiviraj" : "Aktiviraj"}</button>}</td></tr>)}</tbody></table></div>
        <Pager {...tariffs} onPage={setTariffPage} />
      </section>
      <section className="panel table-panel"><h2>Računi</h2>
        {allowed("Invoice.View") && <button type="button" disabled={busy || !period} onClick={() => void loadInvoices()}>Prikaži račune za period</button>}
        <div className="table-scroll"><table><thead><tr><th>Broj računa</th><th>Period</th><th>Jedinica</th><th>Vlasnik</th><th>Izdato</th><th>Dospijeće</th><th>Iznos pri izdavanju</th><th>Efektivno</th><th>Plaćeno</th><th>Otvoreno</th><th>Status</th><th>Dokumenti</th></tr></thead>
          <tbody>{invoices.items.map((invoice) => <tr key={`${invoice.propertyUnitId}-${invoice.personId}`}>
            <td>{invoiceListView(invoice, period).number}</td><td>{invoiceListView(invoice, period).period}</td><td>{invoice.unitCode}</td>
            <td>{invoiceListView(invoice, period).owner}</td><td>{invoice.issueDate ?? "—"}</td><td>{invoice.dueDate ?? "—"}</td>
            <td>{invoiceListView(invoice, period).total}</td><td>{formatMoney(invoice.effectiveTotal ?? invoice.total, invoice.currencyCode)}</td>
            <td>{invoiceListView(invoice, period).paid}</td><td>{invoiceListView(invoice, period).open}</td>
            <td>{invoiceListView(invoice, period).status}</td>
            <td><button type="button" disabled={busy} onClick={() => setSelectedInvoice(invoice)}>Detalji/uplatnice</button>
              {allowed("Invoice.Export") && <button type="button" disabled={busy} onClick={() => void downloadInvoice(invoice)}>PDF račun</button>}
              {allowed("OwnerLedger.View") && <button type="button" disabled={busy} onClick={() => void loadLedger(invoice)}>Kartica jedinice</button>}
              {allowed("OwnerLedger.View") && <button type="button" disabled={busy}
                onClick={() => { setOwnerCardPage(1); setOwnerCardPersonId(invoice.personId); }}>Kartica vlasnika</button>}</td></tr>)}</tbody></table></div>
        {invoices.items.length === 0 && <p className="empty-inline">Nema računa za odabrani period.</p>}
        <Pager {...invoices} onPage={setInvoicePage} />
      </section>
      {selectedInvoice && invoiceDetail && <section className="panel"><h2>Račun {invoiceDetail.invoiceNumber ?? "(stari obračun, informativno)"} · {selectedInvoice.unitCode}</h2>
        <p>Primalac: {invoiceDetail.personName} · Izdato: {invoiceDetail.issueDate ?? "—"} · Dospijeće: {invoiceDetail.dueDate ?? "—"}</p>
        <p>Prethodni saldo pri izdavanju: {formatMoney(invoiceDetail.previousOpen, invoiceDetail.currency)} · Novo zaduženje: {formatMoney(invoiceDetail.currentTotal, invoiceDetail.currency)} · Popust: {formatMoney(invoiceDetail.discountTotal, invoiceDetail.currency)} · Ukupno pri izdavanju: {formatMoney(invoiceDetail.totalDueAtIssue, invoiceDetail.currency)}</p>
        <h3>Stavke</h3><ul>{invoiceDetail.lines.map((line) => <li key={line.id ?? line.code}>
          {line.name} · {line.quantity ?? "—"} {line.unitOfMeasure ?? ""} × {line.unitPrice ?? "—"} ·
          popust {formatMoney(line.discountAmount ?? 0, invoiceDetail.currency)} · {
            formatMoney(line.amount, invoiceDetail.currency)}</li>)}</ul>
        <h3>Trenutno finansijsko stanje</h3>
        {invoiceDetail.documentStatus === "Cancelled" ? <p><strong>STORNIRAN</strong> · Prije storna plaćeno:
          {" "}{formatMoney(invoiceDetail.paidBeforeStorno, invoiceDetail.currency)} · Nakon storna otvoreno: {
            formatMoney(invoiceDetail.totalOpen, invoiceDetail.currency)}
          {" "}· Kredit nastao stornom: {formatMoney(invoiceDetail.stornoCredit, invoiceDetail.currency)}
          {" "}· Trenutno neraspoređeni kredit osobe: {formatMoney(invoiceDetail.credit, invoiceDetail.currency)}</p>
          : <p>Efektivno zaduženje: {formatMoney(invoiceDetail.effectiveTotal ?? invoiceDetail.currentTotal, invoiceDetail.currency)}
            {" "}· Plaćeno: {formatMoney(invoiceDetail.currentPaid, invoiceDetail.currency)} · Otvoreno: {
              formatMoney(invoiceDetail.totalOpen, invoiceDetail.currency)}
            {" "}· Neraspoređeni kredit osobe: {formatMoney(invoiceDetail.credit, invoiceDetail.currency)} · {invoiceDetail.paymentStatus}</p>}
        {invoiceDetail.payments.length > 0 && <ul>{invoiceDetail.payments.map((payment) =>
          <li key={`${payment.paymentId}-${payment.paidOn}`}>{payment.paidOn} · {
            formatMoney(payment.amount, invoiceDetail.currency)}</li>)}</ul>}
        <p>Poziv na broj: {invoiceDetail.paymentReference}</p>
        {invoiceDetail.accounts.map((account) => <div className="fund-row" key={account.fundId}>
          <span>{account.fundName} · {formatMoney(account.currentAmount, account.currencyCode)} · {account.accountNumber}</span>
          {allowed("Invoice.Export") && invoiceDetail.documentStatus !== "Cancelled" &&
            <button type="button" disabled={busy} onClick={() => void downloadSlip(selectedInvoice, account.fundId)}>Uplatnica</button>}
        </div>)}
        <p>QR plaćanje nije konfigurirano. Za različite fondove koristite odgovarajuće račune/uplatnice.</p>
        <InvoiceDocuments zevId={zevId} invoiceId={selectedInvoice.id}
          currencyCode={invoiceDetail.currency}
          status={invoiceDetail.documentStatus} token={token} canReverse={allowed("Billing.Reverse")}
          canAmend={allowed("Invoice.Correct")} lines={invoiceDetail.lines} funds={funds}
          onChanged={() => { void loadInvoices(); }} />
      </section>}
      {ledger && <section className="panel table-panel"><h2>Kartica jedinice / vlasnika</h2>
        <div className="admin-form"><label>Događaji od<input type="date" value={ledgerFrom}
          onChange={(event) => { setLedgerFrom(event.target.value); setLedgerPage(1); }} /></label>
          <label>Stanje do<input type="date" value={ledgerTo}
            onChange={(event) => { setLedgerTo(event.target.value); setLedgerPage(1); }} /></label></div>
        {ledger.balancesByCurrency.map((balance) => <p key={balance.currencyCode}>
          <strong>{balance.currencyCode}</strong> · Zaduženo: {formatMoney(balance.charges, balance.currencyCode)}
          {" "}· Plaćeno: {formatMoney(balance.paid, balance.currencyCode)}
          {" "}· Otvoreno: {formatMoney(balance.open, balance.currencyCode)}
          {" "}· Saldo: {formatMoney(balance.balance, balance.currencyCode)}</p>)}
        <div className="table-scroll"><table><thead><tr><th>Datum</th><th>Vrsta</th><th>Dokument</th><th>Opis</th><th>Promjena</th><th>Saldo</th></tr></thead>
          <tbody>{ledger.items.map((item, index) => <tr key={`${item.date}-${index}`}><td>{item.date}</td><td>{ownerLedgerLabel(item.kind)}</td>
            <td>{item.document}</td><td>{item.description}</td>
            <td>{formatMoney(item.signedAmount, item.currencyCode)}</td>
            <td>{formatMoney(item.balance, item.currencyCode)}</td></tr>)}</tbody></table></div>
        <Pager page={ledger.page} pageSize={ledger.pageSize} totalCount={ledger.totalCount} onPage={setLedgerPage} />
      </section>}
      {ownerCard && <section className="panel table-panel"><h2>Kartica vlasnika</h2>
        <div className="admin-form"><label>Događaji od<input type="date" value={ownerCardFrom}
          onChange={(event) => { setOwnerCardFrom(event.target.value); setOwnerCardPage(1); }} /></label>
          <label>Stanje do<input type="date" value={ownerCardTo}
            onChange={(event) => { setOwnerCardTo(event.target.value); setOwnerCardPage(1); }} /></label></div>
        <p>Saldo je stanje do odabranog datuma; datum „od” ograničava samo listu događaja.</p>
        {ownerCard.balances.length === 0 && <p>Nema finansijskih događaja.</p>}
        {ownerCard.balances.map((balance) => <p key={balance.currencyCode}><strong>{balance.currencyCode}</strong>
          {" "}· Zaduženo: {formatMoney(balance.totalCharges, balance.currencyCode)}
          {" "}· Uplaćeno: {formatMoney(balance.totalPayments, balance.currencyCode)}
          {" "}· Otvoreno: {formatMoney(balance.open, balance.currencyCode)}
          {" "}· Kredit: {formatMoney(balance.credit, balance.currencyCode)}
          {" "}· Neto saldo: {formatMoney(balance.netBalance, balance.currencyCode)}</p>)}
        <div className="table-scroll"><table><thead><tr><th>Datum</th><th>Dokument</th><th>Vrsta</th><th>Opis</th>
          <th>Zaduženje</th><th>Uplata</th><th>Promjena kredita</th><th>Saldo</th></tr></thead>
          <tbody>{ownerCard.events.map((item, index) => <tr key={`${item.document}-${index}`}><td>{item.date}</td>
            <td>{item.document}</td><td>{ownerLedgerLabel(item.kind)}</td><td>{item.description}</td>
            <td>{formatMoney(item.debt, item.currencyCode)}</td><td>{formatMoney(item.payment, item.currencyCode)}</td>
            <td>{formatMoney(item.creditDelta, item.currencyCode)}</td>
            <td>{formatMoney(item.runningBalance, item.currencyCode)}</td></tr>)}</tbody></table></div>
        <Pager page={ownerCard.page} pageSize={ownerCard.pageSize}
          totalCount={ownerCard.totalCount} onPage={setOwnerCardPage} />
      </section>}
      <section className="panel table-panel"><h2>Zaduženja</h2>
        <div className="table-scroll"><table><thead><tr><th>Vlasnik</th><th>Period</th><th>Iznos</th><th>Dospijeće</th><th>Poziv na broj</th></tr></thead>
          <tbody>{charges.items.map((item) => <tr key={item.id}><td>{item.personName}</td><td>{item.period}</td>
            <td>{formatMoney(item.amount, item.currencyCode)}</td><td>{item.dueDate}</td><td>{item.reference}</td></tr>)}</tbody></table></div>
        <Pager {...charges} onPage={setChargePage} />
      </section>
    </>}
  </>;
}
