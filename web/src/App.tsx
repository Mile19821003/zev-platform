import { FormEvent, useEffect, useState } from "react";
import Workspace from "./admin/Workspace";
import RoleWorkspace from "./admin/RoleWorkspace";
import PasswordChangePage from "./PasswordChangePage";
import { ApiError, callApi, errorText, Profile, Zev, Page } from "./api";
import { useChangeRevision } from "./useChangeRevision.ts";
import { refreshVisibleData } from "./dataSync.ts";

type TokenResponse = { accessToken: string; expiresUtc: string };

async function accessibleZevs(token: string): Promise<Zev[]> {
  const all: Zev[] = [];
  for (let page = 1; ; page++) {
    const result = await callApi<Page<Zev>>(`/api/me/zevs?page=${page}&pageSize=100`, {}, token);
    all.push(...result.items);
    if (all.length >= result.totalCount || result.items.length === 0) return all;
  }
}

export default function App() {
  const [token, setToken] = useState<string | null>(null);
  const [profile, setProfile] = useState<Profile | null>(null);
  const [zevs, setZevs] = useState<Zev[]>([]);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [profilePage, setProfilePage] = useState(false);
  const revision = useChangeRevision("organizations", "zevs", "access");

  useEffect(() => {
    const refetch = () => { if (document.visibilityState === "visible") refreshVisibleData(); };
    window.addEventListener("focus", refetch);
    document.addEventListener("visibilitychange", refetch);
    return () => { window.removeEventListener("focus", refetch); document.removeEventListener("visibilitychange", refetch); };
  }, []);

  useEffect(() => {
    let active = true;
    callApi<TokenResponse>("/api/auth/refresh", { method: "POST" })
      .then((result) => { if (active) setToken(result.accessToken); })
      .catch(() => {})
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!token) return;
    let active = true;
    callApi<Profile>("/api/me", {}, token).then(async (user) => {
      const availableZevs = user.mustChangePassword ? [] : await accessibleZevs(token);
      if (active) { setProfile(user); setZevs(availableZevs); setError(""); }
    }).catch(async (problem) => {
      if (!active) return;
      if (problem instanceof ApiError && problem.status === 401) {
        try {
          const refreshed = await callApi<TokenResponse>("/api/auth/refresh", { method: "POST" });
          if (active) setToken(refreshed.accessToken);
          return;
        } catch { if (active) { setToken(null); setProfile(null); setZevs([]); } }
      }
      if (active) { setError(errorText(problem)); if (!profile) setToken(null); }
    });
    return () => { active = false; };
  }, [token, revision]);

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setLoading(true);
    try {
      const result = await callApi<TokenResponse>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password }),
      });
      setToken(result.accessToken);
      setPassword("");
    } catch {
      setError("Prijava nije uspjela. Provjerite podatke i pokušajte ponovo.");
    } finally {
      setLoading(false);
    }
  }

  async function logout() {
    try { await callApi<void>("/api/auth/logout", { method: "POST" }); } catch { setError(""); }
    setToken(null);
    setProfile(null);
    setZevs([]);
    setProfilePage(false);
  }

  function passwordChanged(nextToken: string) {
    setProfile(null);
    setProfilePage(false);
    setToken(nextToken);
  }

  if ((loading || token) && !profile) return <div className="loading">Učitavanje…</div>;

  if (!profile) return (
    <main className="login-page">
      <section className="login-intro">
        <div className="brand-mark">Z</div>
        <p className="eyebrow">ZEV PLATFORMA</p>
        <h1>Upravljanje zajednicom, na jednom mjestu.</h1>
        <p>Siguran prostor za upravnike, predstavnike i etažne vlasnike.</p>
      </section>
      <section className="login-card" aria-labelledby="login-heading">
        <p className="eyebrow">DOBRO DOŠLI</p>
        <h2 id="login-heading">Prijavite se</h2>
        <p>Unesite korisnički nalog koji vam je dodijeljen.</p>
        <form onSubmit={login}>
          <label htmlFor="email">Email adresa</label>
          <input id="email" type="email" autoComplete="username" required value={email} onChange={(event) => setEmail(event.target.value)} />
          <label htmlFor="password">Lozinka</label>
          <input id="password" type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
          {error && <p className="error" role="alert">{error}</p>}
          <button type="submit" disabled={loading}>{loading ? "Prijava…" : "Prijavi se"}</button>
        </form>
      </section>
    </main>
  );

  if (token && (profile.mustChangePassword || profilePage)) {
    return <PasswordChangePage token={token} email={profile.email}
      forced={profile.mustChangePassword} onChanged={passwordChanged}
      onCancel={() => setProfilePage(false)} onLogout={logout} />;
  }

  if (profile.isSystemAdministrator && token) {
    return <Workspace token={token} profile={profile} zevs={zevs} onLogout={logout}
      onProfile={() => setProfilePage(true)} />;
  }

  if (token) return <RoleWorkspace token={token} profile={profile} zevs={zevs} onLogout={logout}
    onProfile={() => setProfilePage(true)} />;

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark small">Z</span><span>ZEV platforma</span></div>
        <nav aria-label="Glavna navigacija"><a className="active" href="#pregled">Pregled</a></nav>
        <div className="sidebar-note">Faza 1 · Osnovna platforma</div>
      </aside>
      <div className="content-shell">
        <header className="topbar"><span>Pregled</span><div><span className="user-email">{profile.email}</span><button className="text-button" onClick={logout}>Odjavi se</button></div></header>
        <main id="pregled" className="dashboard">
          <p className="eyebrow">RADNI PROSTOR</p>
          <h1>Dobro došli</h1>
          <p className="lead">Vaše zajednice i ovlaštenja na jednom mjestu.</p>
          <div className="summary-card"><span>Uloga</span><strong>{profile.isSystemAdministrator ? "Sistemski administrator" : profile.memberships.map((item) => item.role).join(", ") || "Bez dodijeljene uloge"}</strong></div>
          <div className="section-heading"><h2>Zajednice kojima imate pristup</h2><span>{zevs.length} prikazano</span></div>
          <div className="zev-grid">
            {zevs.map((zev) => <article className="zev-card" key={zev.id}><div className="zev-icon">Z</div><p className="eyebrow">{zev.code}</p><h3>{zev.name}</h3><p>Organizacija: {zev.organizationId}</p></article>)}
          </div>
          {zevs.length === 0 && <div className="empty-state">Još nemate dodijeljenu zajednicu.</div>}
          <div className="next-note">Finansije, održavanje, dokumenti i portal vlasnika dolaze u narednim fazama.</div>
        </main>
      </div>
    </div>
  );
}
