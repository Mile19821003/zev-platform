import { useEffect, useRef, useState } from "react";
import { callApi, errorText, Profile, Zev } from "../api";
import OrganizationsPage from "./OrganizationsPage";
import OwnershipPage from "./OwnershipPage";
import FinancePage from "./FinancePage";
import PaymentsPage from "./PaymentsPage";
import ExpensesPage from "./ExpensesPage";
import LegalPage from "./LegalPage";
import MaintenancePage from "./MaintenancePage";
import HodogramPage from "./HodogramPage";
import CommunityPage from "./CommunityPage";
import ReportsPage from "./ReportsPage";
import ResourcePage, { ResourceConfig } from "./ResourcePage";
import ZevPermissionPage from "./ZevPermissionPage";
import AccessControlPage from "./AccessControlPage";
import SystemSettingsPage from "./SystemSettingsPage";
import LookupsPage from "./LookupsPage";
import IconSettingsPage from "./IconSettingsPage";
import SystemErrorsPage from "./SystemErrorsPage";
import VersionHistoryPage from "./VersionHistoryPage";
import DeletedRecordsPage from "./DeletedRecordsPage";
import ThemeSettingsPage from "./ThemeSettingsPage";
import ZevCardGrid from "./ZevCards";
import { roleLabels } from "../uiLabels.ts";
import NotificationBell from "./NotificationBell";
import MaintenanceWidget from "./MaintenanceWidget";
import { useChangeRevision } from "../useChangeRevision.ts";
import WorkspaceNav, { useSidebarState } from "./WorkspaceNav";
import ModuleDashboard from "./ModuleDashboard";
import { moduleLabel } from "../navigationModel.ts";

type MenuKey = "overview" | "organizations" | "zevs" | "buildings" | "entrances" | "units"
  | "people" | "ownership" | "users" | "memberships" | "accountLinks" | "settings" | "lookups" | "icons" | "errors" | "versions" | "deleted" | "themes" | "finance" | "payments" | "expenses" | "legal" | "maintenance" | "hodogram" | "community" | "reports";

const menu: { key: MenuKey; label: string }[] = [
  { key: "overview", label: "Pregled" },
  { key: "organizations", label: "Organizacije / upravnici" },
  { key: "zevs", label: "ZEV" },
  { key: "buildings", label: "Zgrade" },
  { key: "entrances", label: "Ulazi" },
  { key: "units", label: "Posebni dijelovi" },
  { key: "people", label: "Vlasnici / korisnici" },
  { key: "ownership", label: "Vlasništvo i korisnici" },
  { key: "users", label: "Korisnici sistema" },
  { key: "memberships", label: "Uloge i pristupi" },
  { key: "accountLinks", label: "Pristupi i veze naloga" },
  { key: "lookups", label: "Šifarnici" },
  { key: "settings", label: "Sistemska podešavanja" },
  { key: "icons", label: "Prikazi i ikone" },
  { key: "themes", label: "Teme i paleta boja" },
  { key: "errors", label: "Greške u sistemu" },
  { key: "versions", label: "Istorija verzija" },
  { key: "deleted", label: "Obrisani redovi" },
  { key: "finance", label: "Finansije" },
  { key: "payments", label: "Uplate i izvodi" },
  { key: "expenses", label: "Rashodi i dugovanja" },
  { key: "legal", label: "Opomene i pravni predmeti" },
  { key: "maintenance", label: "Održavanje" },
  { key: "hodogram", label: "Hodogram" },
  { key: "community", label: "Zajednica i glasanje" },
  { key: "reports", label: "Izvještaji" },
];

const resources: Partial<Record<MenuKey, ResourceConfig>> = {
  zevs: {
    title: "ZEV", description: "Zajednice i njihovi osnovni podaci.", path: "/api/system/zevs",
    image: { label: "Fotografija zgrade", path: (id) => `/api/zevs/${id}/image`, urlKey: "imageUrl", nameKey: "name" },
    fields: [
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, createOnly: true, lookupPath: () => "/api/system/organizations" },
      { key: "code", label: "Šifra", required: true, maxLength: 50 },
      { key: "name", label: "Naziv", required: true, maxLength: 200 },
      { key: "addressLine", label: "Adresa", maxLength: 250 },
      { key: "cityMunicipality", label: "Grad / opština", maxLength: 120 },
      { key: "taxIdentifier", label: "JIB / PIB", maxLength: 32 },
      { key: "primaryBankAccount", label: "Glavni bankovni račun", maxLength: 64 },
      { key: "reserveFundBankAccount", label: "Račun rezervnog fonda", maxLength: 64 },
      { key: "contactEmail", label: "Kontakt email", kind: "email", maxLength: 320 },
      { key: "contactPhone", label: "Kontakt telefon", maxLength: 40 },
      { key: "foundedOn", label: "Datum osnivanja", kind: "date" },
    ],
    columns: [{ key: "code", label: "Šifra" }, { key: "name", label: "Naziv" }, { key: "organizationName", label: "Upravnik" }],
    allowEdit: true, allowStatus: true, search: true,
  },
  buildings: {
    title: "Zgrade", description: "Zgrade povezane sa zajednicama.", path: "/api/system/buildings",
    fields: [
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, createOnly: true, formOnly: true,
        lookupPath: () => "/api/system/organizations" },
      { key: "zevId", label: "ZEV", kind: "lookup", required: true, createOnly: true, dependsOn: "organizationId",
        lookupPath: (form) => form.organizationId ? `/api/system/zevs?organizationId=${form.organizationId}` : "" },
      { key: "code", label: "Šifra", required: true, maxLength: 50 },
      { key: "name", label: "Naziv", required: true, maxLength: 200 },
      { key: "addressLine", label: "Adresa", maxLength: 250 },
    ],
    columns: [{ key: "code", label: "Šifra" }, { key: "name", label: "Naziv" }, { key: "zevName", label: "ZEV" }],
    allowEdit: true, allowStatus: true, allowDelete: true, search: true,
  },
  entrances: {
    title: "Ulazi", description: "Ulazi svake zgrade.", path: "/api/system/entrances",
    fields: [
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, createOnly: true, formOnly: true,
        lookupPath: () => "/api/system/organizations" },
      { key: "zevId", label: "ZEV", kind: "lookup", required: true, createOnly: true, formOnly: true, dependsOn: "organizationId",
        lookupPath: (form) => form.organizationId ? `/api/system/zevs?organizationId=${form.organizationId}` : "" },
      { key: "buildingId", label: "Zgrada", kind: "lookup", required: true, createOnly: true, dependsOn: "zevId",
        lookupPath: (form) => form.zevId ? `/api/system/buildings?zevId=${form.zevId}` : "" },
      { key: "code", label: "Šifra", required: true, maxLength: 50 },
      { key: "name", label: "Naziv", required: true, maxLength: 200 },
    ],
    columns: [{ key: "code", label: "Šifra" }, { key: "name", label: "Naziv" }, { key: "buildingName", label: "Zgrada" }],
    allowEdit: true, allowStatus: true, allowDelete: true, search: true,
  },
  units: {
    title: "Posebni dijelovi", description: "Stanovi, poslovni prostori, garaže i podrumi.", path: "/api/system/property-units",
    fields: [
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, createOnly: true, formOnly: true,
        lookupPath: () => "/api/system/organizations" },
      { key: "zevId", label: "ZEV", kind: "lookup", required: true, createOnly: true, formOnly: true, dependsOn: "organizationId",
        lookupPath: (form) => form.organizationId ? `/api/system/zevs?organizationId=${form.organizationId}` : "" },
      { key: "buildingId", label: "Zgrada", kind: "lookup", required: true, createOnly: true, formOnly: true, dependsOn: "zevId",
        lookupPath: (form) => form.zevId ? `/api/system/buildings?zevId=${form.zevId}` : "" },
      { key: "entranceId", label: "Ulaz", kind: "lookup", required: true, createOnly: true, dependsOn: "buildingId",
        lookupPath: (form) => form.buildingId ? `/api/system/entrances?buildingId=${form.buildingId}` : "" },
      { key: "code", label: "Oznaka", required: true, maxLength: 50 },
      { key: "type", label: "Tip", required: true, lookupType: "PROPERTY_UNIT_TYPE" },
      { key: "floor", label: "Sprat", kind: "number" },
      { key: "areaSquareMeters", label: "Površina (m²)", kind: "number", required: true, step: "0.001" },
      { key: "coefficient", label: "Koeficijent", kind: "number", required: true, step: "0.0001" },
      { key: "registeredMembers", label: "Prijavljeni članovi", kind: "number", required: true, step: "1" },
    ],
    columns: [{ key: "code", label: "Oznaka" }, { key: "type", label: "Tip", lookupType: "PROPERTY_UNIT_TYPE" }, { key: "entranceName", label: "Ulaz" },
      { key: "areaSquareMeters", label: "m²" }],
    allowEdit: true, allowStatus: true, allowDelete: true, search: true,
  },
  people: {
    title: "Vlasnici / korisnici", description: "Fizička i pravna lica povezana sa organizacijom.", path: "/api/system/people",
    fields: [
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, createOnly: true, lookupPath: () => "/api/system/organizations" },
      { key: "kind", label: "Vrsta lica", kind: "select", required: true, options: ["Individual", "LegalEntity"] },
      { key: "displayName", label: "Ime / naziv", required: true, maxLength: 200 },
      { key: "userId", label: "Korisnički nalog", kind: "lookup", lookupPath: () => "/api/system/users" },
      { key: "taxIdentifier", label: "JIB / PIB", maxLength: 32 },
      { key: "addressLine", label: "Adresa", maxLength: 250 },
      { key: "cityMunicipality", label: "Grad / opština", maxLength: 120 },
      { key: "email", label: "Email", kind: "email", maxLength: 320 },
      { key: "phone", label: "Telefon", maxLength: 40 },
    ],
    columns: [{ key: "displayName", label: "Ime / naziv" }, { key: "kind", label: "Vrsta" }, { key: "email", label: "Email" }],
    allowEdit: true, allowDelete: true, search: true,
  },
  users: {
    title: "Korisnici sistema", description: "Prijavni nalozi i status pristupa.", path: "/api/system/users",
    fields: [{ key: "email", label: "Email", kind: "email", required: true, maxLength: 320 },
      { key: "password", label: "Početna lozinka", kind: "password", required: true }],
    columns: [{ key: "email", label: "Email" }], allowStatus: true, passwordReset: true, search: true,
  },
  memberships: {
    title: "Ručne uloge", description: "Owner pristup se izvodi iz verifikovanog vlasništva i ne dodjeljuje kao članstvo.", path: "/api/system/memberships",
    fields: [
      { key: "userId", label: "Korisnik", kind: "lookup", required: true, lookupPath: () => "/api/system/users" },
      { key: "organizationId", label: "Organizacija", kind: "lookup", required: true, lookupPath: () => "/api/system/organizations" },
      { key: "zevId", label: "ZEV (prazno samo za administratora organizacije)", kind: "lookup", dependsOn: "organizationId", lookupPath: (form) =>
        form.organizationId ? `/api/system/zevs?organizationId=${form.organizationId}` : "" },
      { key: "role", label: "Uloga", kind: "select", required: true,
        options: ["OrganizationAdministrator", "Manager", "Accountant", "BuildingRepresentative"], optionLabels: roleLabels },
    ],
    columns: [{ key: "userEmail", label: "Korisnik" }, { key: "organizationName", label: "Organizacija" },
      { key: "zevName", label: "ZEV" }, { key: "role", label: "Uloga", labels: roleLabels }],
    allowStatus: true,
  },
};

export default function Workspace({ token, profile, zevs, onLogout, onProfile }: {
  token: string; profile: Profile; zevs: Zev[]; onLogout: () => void; onProfile: () => void;
}) {
  const [active, setActive] = useState<MenuKey>("overview");
  const [sidebar, setSidebar] = useSidebarState();
  const [contextError, setContextError] = useState("");
  const permissionRequestId = useRef(0);
  const [selectedZevId, setSelectedZevId] = useState(() => sessionStorage.getItem("activeZevId") ?? "");
  const activeZev = zevs.find((item) => item.id === selectedZevId) ?? zevs[0];
  const scopedZevs = activeZev ? [activeZev] : [];
  const [revision, setRevision] = useState(0);
  const [overview, setOverview] = useState<{ organizations: number; zevs: number; activeZevs: number } | null>(null);
  const [error, setError] = useState("");
  const dataRevision = useChangeRevision("overview");

  function selectZev(id: string) {
    setSelectedZevId(id); sessionStorage.setItem("activeZevId", id);
    setActive("overview");
    setContextError("");
    const requestId = ++permissionRequestId.current;
    void callApi(`/api/zevs/${id}/access/permissions`, {}, token)
      .catch((problem) => { if (permissionRequestId.current === requestId) setContextError(errorText(problem)); });
  }

  useEffect(() => {
    if (active !== "overview") return;
    callApi<{ organizations: number; zevs: number; activeZevs: number }>("/api/system/overview", {}, token)
      .then((result) => { setOverview(result); setError(""); })
      .catch((problem) => setError(errorText(problem)));
  }, [active, revision, dataRevision, token]);

  const activeItem = menu.find((item) => item.key === active);
  return <div className={`app-shell${sidebar.collapsed ? " nav-collapsed" : ""}`}>
    <WorkspaceNav label="Administracija" items={menu} active={active} onSelect={(key) => setActive(key as MenuKey)}
      note="Sistemska administracija" state={sidebar} onState={setSidebar} />
    <div className="content-shell">
      <header className="topbar"><span>{activeItem && moduleLabel(activeItem)}</span><div>
        <label>Trenutni ZEV: <select aria-label="Trenutni ZEV" value={activeZev?.id ?? ""}
          onChange={(event) => selectZev(event.target.value)}>
          {zevs.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        {activeZev && <NotificationBell token={token} zevId={activeZev.id} onOpen={() => setActive("maintenance")} />}
        <span className="user-email">{profile.email}</span><button type="button" className="text-button" onClick={onProfile}>Moj profil</button><button type="button" className="text-button" onClick={onLogout}>Odjavi se</button>
      </div></header>
      <main className="dashboard admin-main">
        {contextError && <p className="error" role="alert">{contextError}</p>}
        {active === "overview" && <>
          <section className="zev-home" aria-labelledby="zev-home-heading">
            <div className="section-heading"><h2 id="zev-home-heading">Dostupne zajednice</h2>
              <span>{zevs.length} prikazano · klik na karticu otvara radni prostor</span></div>
            <ZevCardGrid token={token} zevs={zevs} activeId={activeZev?.id ?? null} onSelect={selectZev} />
            {zevs.length === 0 && <div className="empty-state">Još nema aktivnih zajednica.</div>}
          </section>
          {activeZev && <MaintenanceWidget token={token} zevId={activeZev.id} onOpen={() => setActive("maintenance")} />}
          <ModuleDashboard items={menu} onSelect={(key) => setActive(key as MenuKey)}
            title={activeZev ? `Moduli · ${activeZev.name}` : "Moduli"} />
          <p className="eyebrow">SISTEM</p><h1>Pregled platforme</h1>
          <p className="lead">Organizacije, ZEV-ovi i pristupi na jednom mjestu.</p>
          {error && <p role="alert" className="error">{error}</p>}
          <div className="overview-grid"><div className="summary-card"><span>Organizacije</span><strong>{overview?.organizations ?? "…"}</strong></div>
            <div className="summary-card"><span>ZEV-ovi</span><strong>{overview?.zevs ?? "…"}</strong></div>
            <div className="summary-card"><span>Aktivni ZEV-ovi</span><strong>{overview?.activeZevs ?? "…"}</strong></div></div></>}
        {active === "organizations" && <OrganizationsPage token={token} onChanged={() => setRevision((value) => value + 1)} />}
        {active === "ownership" && <OwnershipPage token={token} />}
        {active === "finance" && <FinancePage token={token} zevs={scopedZevs} />}
        {active === "payments" && <PaymentsPage token={token} zevs={scopedZevs} />}
        {active === "expenses" && <ExpensesPage token={token} zevs={scopedZevs} />}
        {active === "legal" && <LegalPage token={token} zevs={scopedZevs} />}
        {active === "maintenance" && <MaintenancePage token={token} zevs={scopedZevs} role="manager" />}
        {active === "hodogram" && <HodogramPage token={token} organizations={null} />}
        {active === "community" && <CommunityPage token={token} zevs={scopedZevs} role="manager" />}
        {active === "reports" && <ReportsPage token={token} zevs={scopedZevs} />}
        {resources[active] && <ResourcePage key={active} config={resources[active]} token={token}
          onChanged={() => setRevision((value) => value + 1)} />}
        {active === "memberships" && activeZev && <ZevPermissionPage token={token} zevId={activeZev.id} zevs={zevs} />}
        {active === "accountLinks" && activeZev && <AccessControlPage token={token} zevId={activeZev.id} zevs={zevs} />}
        {active === "settings" && <SystemSettingsPage token={token} onOpenThemes={() => setActive("themes")} />}
        {active === "lookups" && <LookupsPage token={token} />}
        {active === "icons" && <IconSettingsPage token={token} />}
        {active === "errors" && <SystemErrorsPage token={token} />}
        {active === "versions" && <VersionHistoryPage token={token} />}
        {active === "deleted" && <DeletedRecordsPage token={token} />}
        {active === "themes" && <ThemeSettingsPage token={token} />}
      </main>
    </div>
  </div>;
}
