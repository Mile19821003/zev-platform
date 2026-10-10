import { useEffect, useRef, useState } from "react";
import { callApi, errorText, Profile, Zev } from "../api";
import FinancePage from "./FinancePage";
import PaymentsPage from "./PaymentsPage";
import BankTransactionsPage from "./BankTransactionsPage";
import ExpensesPage from "./ExpensesPage";
import LegalPage from "./LegalPage";
import MaintenancePage from "./MaintenancePage";
import HodogramPage from "./HodogramPage";
import CommunityPage from "./CommunityPage";
import ReportsPage from "./ReportsPage";
import OwnerFinancePage from "./OwnerFinancePage";
import ManagerRegistryPage from "./ManagerRegistryPage";
import { hasStaffPermission } from "../permissionUi.ts";
import ZevPermissionPage from "./ZevPermissionPage";

type ZevOption = Zev;
type PageKey = "overview" | "finance" | "payments" | "bank-transactions" | "expenses" | "legal" | "maintenance"
  | "representative-maintenance" | "my-maintenance" | "community" | "representative-community" | "owner-community" | "reports" | "owner-finance" | "registry" | "access" | "hodogram";

export default function RoleWorkspace({ token, profile, zevs, onLogout, onProfile }: {
  token: string; profile: Profile; zevs: ZevOption[]; onLogout: () => void; onProfile: () => void;
}) {
  const [active, setActive] = useState<PageKey>("overview");
  const [workspaceOpen, setWorkspaceOpen] = useState(false);
  const [selectedZevId, setSelectedZevId] = useState(() => sessionStorage.getItem("activeZevId") ?? "");
  const [permissionContext, setPermissionContext] = useState<{ zevId: string; isStaff: boolean;
    permissions: string[] } | null>(null);
  const [contextError, setContextError] = useState("");
  const [administeredOrganizations, setAdministeredOrganizations] = useState<{ id: string; name: string }[]>([]);
  const permissionRequestId = useRef(0);
  const administeredIds = [...new Set(profile.memberships.filter((item) => item.role === "OrganizationAdministrator"
    && !item.zevId).map((item) => item.organizationId))].join(",");
  const baseZev = zevs.find((item) => item.id === selectedZevId) ?? zevs[0];
  const activeZev = baseZev && permissionContext?.zevId === baseZev.id
    ? { ...baseZev, isStaff: permissionContext.isStaff, permissions: permissionContext.permissions } : baseZev;
  const has = (key: string) => hasStaffPermission(activeZev, key);
  const financeZevs = activeZev && has("Finance.View") ? [activeZev] : [];
  const paymentZevs = activeZev && has("Payments.View") ? [activeZev] : [];
  const managerZevs = activeZev?.isStaff ? [activeZev] : [];
  const representativeZevs = activeZev && !activeZev.canManageLegalCases
    && (has("Maintenance.View") || has("Community.View")) ? [activeZev] : [];
  const sharedOnly = Boolean(activeZev && !activeZev.isStaff && activeZev.permissions?.includes("Community.View"));
  const ownerZevs = activeZev && (activeZev.isOwner || sharedOnly) ? [activeZev] : [];
  const ownerFinanceZevs = activeZev && (activeZev.historicalSelfAccess
    || activeZev.isOwner && activeZev.permissions?.includes("Finance.View")) ? [activeZev] : [];
  const hasRepresentative = Boolean(has("Maintenance.View") && !activeZev?.canManageLegalCases);
  const hasRepresentativeCommunity = Boolean(has("Community.View") && !activeZev?.canManageLegalCases);
  const hasOwner = Boolean(activeZev?.isOwner);
  const hasOwnerFinance = ownerFinanceZevs.length > 0;
  const navigation: { key: PageKey; label: string }[] = [{ key: "overview", label: "Pregled" }];
  if (has("Finance.View")) navigation.push({ key: "finance", label: "Finansije" });
  if (has("Payments.View")) navigation.push({ key: "payments", label: "Uplate i izvodi" });
  if (has("Payments.View")) navigation.push({ key: "bank-transactions", label: "Bankovne transakcije" });
  if (has("Expenses.View")) navigation.push({ key: "expenses", label: "Rashodi i dugovanja" });
  if (has("Reports.View")) navigation.push({ key: "reports", label: "Izvještaji" });
  if (has("Legal.View")) navigation.push({ key: "legal", label: "Pravni predmeti" });
  if (has("Registry.View")) navigation.push({ key: "registry", label: "Vlasnici i nekretnine" });
  if (has("Maintenance.View") && activeZev?.canManageLegalCases) navigation.push({ key: "maintenance", label: "Održavanje" });
  if (hasRepresentative) navigation.push({ key: "representative-maintenance", label: "Pregled održavanja" });
  if (hasOwner) navigation.push({ key: "my-maintenance", label: "Moje prijave" });
  if (has("Community.View") && activeZev?.canManageLegalCases) navigation.push({ key: "community", label: "Zajednica" });
  if (has("Access.Manage")) navigation.push({ key: "access", label: "Uloge i pristupi" });
  if (hasRepresentativeCommunity) navigation.push({ key: "representative-community", label: "Oglasna tabla" });
  if (hasOwner || sharedOnly) navigation.push({ key: "owner-community", label: "Dokumenti i glasanje" });
  if (hasOwnerFinance) navigation.splice(1, 0, { key: "owner-finance", label: "Moj portal" });
  if (administeredOrganizations.length > 0) navigation.push({ key: "hodogram", label: "Hodogram" });

  useEffect(() => {
    let current = true;
    const ids = administeredIds.split(",").filter(Boolean);
    Promise.all(ids.map((id) => callApi<{ id: string; name: string }>(`/api/organizations/${id}`, {}, token)
      .catch(() => null)))
      .then((items) => { if (current) setAdministeredOrganizations(items.filter((item) => item !== null)); });
    return () => { current = false; };
  }, [administeredIds, token]);

  useEffect(() => {
    if (!navigation.some((item) => item.key === active)) setActive("overview");
  }, [active, navigation]);

  function selectZev(id: string) {
    setSelectedZevId(id);
    sessionStorage.setItem("activeZevId", id);
    setActive("overview"); setWorkspaceOpen(true);
    setPermissionContext(null); setContextError("");
    const requestId = ++permissionRequestId.current;
    void callApi<{ isStaff: boolean; permissions: { key: string; effective: boolean }[] }>(
      `/api/zevs/${id}/access/permissions`, {}, token)
      .then((result) => { if (permissionRequestId.current === requestId) setPermissionContext({ zevId: id,
        isStaff: result.isStaff, permissions: result.permissions.filter((item) => item.effective).map((item) => item.key) }); })
      .catch((problem) => { if (permissionRequestId.current === requestId) setContextError(errorText(problem)); });
  }

  useEffect(() => { permissionRequestId.current++; setPermissionContext(null); }, [zevs]);

  return <div className="app-shell"><aside className="sidebar">
    <div className="brand"><span className="brand-mark small">Z</span><span>ZEV platforma</span></div>
    <nav aria-label="Radni meni">{navigation.map((item) => <button type="button" key={item.key}
      className={active === item.key ? "active" : ""} onClick={() => setActive(item.key)}>{item.label}</button>)}</nav>
    <div className="sidebar-note">Radni prostor</div>
  </aside><div className="content-shell"><header className="topbar"><span>{navigation.find((item) => item.key === active)?.label}</span>
    <div><label>Trenutni ZEV: <select aria-label="Trenutni ZEV" value={activeZev?.id ?? ""}
      onChange={(event) => selectZev(event.target.value)}>{zevs.map((item) =>
        <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <span className="user-email">{profile.email}</span><button className="text-button" onClick={onProfile}>Moj profil</button><button className="text-button" onClick={onLogout}>Odjavi se</button></div>
  </header><main className="dashboard admin-main">
    {contextError && <p className="error" role="alert">{contextError}</p>}
    {active === "overview" && workspaceOpen && activeZev && <section className="panel"><p className="eyebrow">RADNI PROSTOR</p>
      <h1>{activeZev.name}</h1><p>{activeZev.addressLine}</p><p>Trenutni ZEV: {activeZev.name}</p>
      <button type="button" onClick={() => setWorkspaceOpen(false)}>Sve zajednice</button></section>}
    {active === "overview" && !workspaceOpen && <><p className="eyebrow">RADNI PROSTOR</p><h1>Vaše zajednice</h1>
      <p className="lead">Podaci i funkcije su ograničeni na dodijeljene uloge i ZEV-ove.</p>
      <div className="zev-grid">{zevs.map((zev) => <article className="zev-card" key={zev.id}>
        <p className="eyebrow">{zev.code}</p><h3>{zev.name}</h3>
        <p>{zev.addressLine}</p><p>{zev.historicalSelfAccess ? "Samo istorijski finansijski podaci" : zev.sources?.join(", ")}</p>
        <button type="button" onClick={() => selectZev(zev.id)}>Otvori ZEV</button></article>)}</div>
      {zevs.length === 0 && <div className="empty-state">Nema dodijeljenih zajednica.</div>}</>}
    {active === "finance" && <FinancePage key={activeZev?.id} token={token} zevs={financeZevs} permissions={activeZev?.permissions} />}
    {active === "payments" && <PaymentsPage key={activeZev?.id} token={token} zevs={paymentZevs} permissions={activeZev?.permissions} />}
    {active === "bank-transactions" && <BankTransactionsPage key={activeZev?.id} token={token}
      zevs={paymentZevs} permissions={activeZev?.permissions} />}
    {active === "expenses" && <ExpensesPage key={activeZev?.id} token={token} zevs={activeZev ? [activeZev] : []} permissions={activeZev?.permissions} />}
    {active === "legal" && <LegalPage key={activeZev?.id} token={token} zevs={managerZevs} permissions={activeZev?.permissions} />}
    {active === "maintenance" && <MaintenancePage key={activeZev?.id} token={token} zevs={managerZevs} role="manager" permissions={activeZev?.permissions} />}
    {active === "representative-maintenance" && <MaintenancePage token={token}
      zevs={representativeZevs} role="representative" permissions={activeZev?.permissions} />}
    {active === "my-maintenance" && <MaintenancePage key={activeZev?.id} token={token} zevs={ownerZevs} role="owner" permissions={activeZev?.permissions} />}
    {active === "community" && <CommunityPage key={activeZev?.id} token={token} zevs={managerZevs} role="manager" permissions={activeZev?.permissions} />}
    {active === "representative-community" && <CommunityPage key={activeZev?.id} token={token} zevs={representativeZevs} role="representative" permissions={activeZev?.permissions} />}
    {active === "owner-community" && <CommunityPage key={activeZev?.id} token={token} zevs={ownerZevs} role="owner" permissions={activeZev?.permissions} />}
    {active === "reports" && <ReportsPage key={activeZev?.id} token={token} zevs={activeZev ? [activeZev] : []} permissions={activeZev?.permissions} />}
    {active === "owner-finance" && <OwnerFinancePage token={token} zevs={ownerFinanceZevs} />}
    {active === "registry" && <ManagerRegistryPage key={activeZev?.id} token={token} zevs={managerZevs} permissions={activeZev?.permissions} />}
    {active === "access" && activeZev && <ZevPermissionPage key={activeZev.id} token={token} zevId={activeZev.id} zevs={zevs} />}
    {active === "hodogram" && <HodogramPage token={token} organizations={administeredOrganizations} />}
  </main></div></div>;
}
