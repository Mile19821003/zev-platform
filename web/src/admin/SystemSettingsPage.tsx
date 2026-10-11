import { FormEvent, useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { DisplaySettings, formatDate, formatDateTime, formatMoney, normalizeDisplaySettings } from "../displaySettings.ts";
import { useChangeRevision } from "../useChangeRevision.ts";
import { activeThemeLabel, normalizeTheme, Theme } from "../theme.ts";
import { SectionTitle } from "./shared";

type SettingsResponse = DisplaySettings & { updatedAt: string; updatedByUserId: string | null; defaultAdminEmail: string | null;
  options: { decimalSeparators: string[]; dateFormats: string[]; fontSizes: string[] } };


const separatorLabels: Record<string, string> = { ",": "Zarez (,)", ".": "Tačka (.)" };

// Administration → "Sistemska podešavanja": regional display (system administrators only).
export default function SystemSettingsPage({ token, onOpenThemes }: { token: string; onOpenThemes?: () => void }) {
  const [loaded, setLoaded] = useState<SettingsResponse | null>(null);
  const [draft, setDraft] = useState<DisplaySettings | null>(null);
  const [busy, setBusy] = useState(false);
  const [adminEmail, setAdminEmail] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const revision = useChangeRevision("settings");
  const themeRevision = useChangeRevision("theme");
  const [theme, setTheme] = useState<Theme | null>(null);

  useEffect(() => {
    let current = true;
    callApi<unknown>("/api/system/theme", {}, token).then((result) => { if (current) setTheme(normalizeTheme(result)); })
      .catch(() => { /* the theme line is informative only */ });
    return () => { current = false; };
  }, [token, themeRevision]);

  useEffect(() => {
    let current = true;
    callApi<SettingsResponse>("/api/system/settings", {}, token)
      .then((result) => { if (current) { setLoaded(result); setDraft(normalizeDisplaySettings(result)); setAdminEmail(result.defaultAdminEmail ?? ""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, revision]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || !loaded) return;
    setBusy(true); setError(""); setNotice("");
    try {
      // The successful PUT publishes the "settings" change; the app reloads and applies it without F5.
      await callApi<SettingsResponse>("/api/system/settings", { method: "PUT",
        body: JSON.stringify({ ...draft, defaultAdminEmail: adminEmail.trim() || null, updatedAt: loaded.updatedAt }) }, token);
      setNotice("Podešavanja su snimljena i primijenjena.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  if (!loaded || !draft) return <>{error ? <p className="error" role="alert">{error}</p> : <p>Učitavanje…</p>}</>;
  const set = (patch: Partial<DisplaySettings>) => { setDraft({ ...draft, ...patch }); setNotice(""); };
  return <>
    <SectionTitle title="Sistemska podešavanja" description="Regionalni prikaz iznosa, datuma i veličine slova za cijelu platformu." />
    <section className="panel settings-panel"><h2>Regionalni prikaz</h2>
      <form className="admin-form" onSubmit={save}>
        <label>Decimalni separator<select value={draft.decimalSeparator}
          onChange={(event) => set({ decimalSeparator: event.target.value as DisplaySettings["decimalSeparator"] })}>
          {loaded.options.decimalSeparators.map((item) => <option key={item} value={item}>{separatorLabels[item] ?? item}</option>)}
        </select></label>
        <label>Format datuma<select value={draft.dateFormat}
          onChange={(event) => set({ dateFormat: event.target.value as DisplaySettings["dateFormat"] })}>
          {loaded.options.dateFormats.map((item) => <option key={item} value={item}>{item}</option>)}</select></label>
        <label>Veličina slova<select value={draft.fontSize} onChange={(event) => set({ fontSize: event.target.value })}>
          {loaded.options.fontSizes.map((item) => <option key={item} value={item}>{item.replace("px", " px")}</option>)}
        </select></label>
        <label>E-mail za obavještenja (nove prijave kvarova)<input type="email" maxLength={320} value={adminEmail}
          placeholder="npr. odrzavanje@uprava.ba" onChange={(event) => { setAdminEmail(event.target.value); setNotice(""); }} /></label>
        <p className="settings-preview" aria-live="polite">Primjer: {formatMoney(1234.5, "BAM", draft)} · {formatDate("2026-10-31", draft)}</p>
        <button type="submit" disabled={busy}>{busy ? "Snimanje…" : "Snimi podešavanja"}</button>
      </form>
      {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
      <p className="empty-inline">Posljednja izmjena: {loaded.updatedByUserId ? formatDateTime(loaded.updatedAt) : "početne vrijednosti"}</p>
    </section>
    <section className="panel settings-panel"><h2>Tema</h2>
      <p>Aktivna tema: <strong>{theme ? activeThemeLabel(theme) : "…"}</strong></p>
      <p className="empty-inline">Boje i tema se podešavaju na jednom mjestu — u „Teme i paleta boja“.</p>
      {onOpenThemes && <button type="button" className="secondary" onClick={onOpenThemes}>Otvori Teme i paletu boja</button>}
    </section>
  </>;
}
