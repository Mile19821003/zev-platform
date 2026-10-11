import { useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { colorFields, ColorKey, contrastWarnings, customThemeName, factoryColors, isHexColor, matchingPreset,
  normalizeTheme, previewStyle, sameColors, Theme, ThemeColors } from "../theme.ts";
import { previewTheme } from "../useTheme.ts";
import { SectionTitle } from "./shared";

// Administration → "Teme i paleta boja": colour pickers with a live preview of the whole app before saving.
export default function ThemeSettingsPage({ token }: { token: string }) {
  const [saved, setSaved] = useState<Theme | null>(null);
  const [draft, setDraft] = useState<ThemeColors>(factoryColors);
  const [hexText, setHexText] = useState<Partial<Record<ColorKey, string>>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let current = true;
    callApi<unknown>("/api/system/theme", {}, token)
      .then((result) => { if (current) { const theme = normalizeTheme(result); setSaved(theme); setDraft(theme.colors); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token]);

  // Live preview of the draft in the whole app; leaving the screen shows the saved palette again.
  useEffect(() => { if (saved) previewTheme(sameColors(draft, saved.colors) ? null : draft); }, [draft, saved]);
  useEffect(() => () => previewTheme(null), []);

  const presets = saved?.presets ?? [];
  const presetName = matchingPreset(draft, presets);
  const dirty = saved !== null && !sameColors(draft, saved.colors);
  const warnings = contrastWarnings(draft);

  function setColor(key: ColorKey, value: string) {
    setHexText((current) => ({ ...current, [key]: value }));
    if (isHexColor(value)) setDraft((current) => ({ ...current, [key]: value.toLowerCase() }));
    setNotice("");
  }

  function choosePreset(name: string) {
    const preset = presets.find((item) => item.name === name);
    if (preset) { setDraft(preset.colors); setHexText({}); setNotice(""); }
  }

  async function save() {
    if (!saved) return;
    setBusy(true); setError(""); setNotice("");
    try {
      const result = await callApi<unknown>("/api/system/theme", { method: "PUT",
        body: JSON.stringify({ activeThemeName: presetName, ...draft, updatedAt: saved.updatedAt }) }, token);
      const theme = normalizeTheme(result);
      setSaved(theme); setDraft(theme.colors); setHexText({});
      setNotice("Boje su snimljene i primijenjene za sve korisnike.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  function discard() {
    if (saved) { setDraft(saved.colors); setHexText({}); setNotice("Izmjene su odbačene."); }
  }

  async function reset() {
    if (!saved || !window.confirm("Vratiti tvorničke boje za sve korisnike?")) return;
    setBusy(true); setError(""); setNotice("");
    try {
      const result = await callApi<unknown>("/api/system/theme/reset", { method: "POST",
        body: JSON.stringify({ updatedAt: saved.updatedAt }) }, token);
      const theme = normalizeTheme(result);
      setSaved(theme); setDraft(theme.colors); setHexText({});
      setNotice("Vraćene su tvorničke boje.");
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  const title = <SectionTitle title="Teme i paleta boja" description="Boje aplikacije za sve korisnike. Izmjene se odmah vide u cijeloj aplikaciji (pregled), a primjenjuju se za sve tek nakon „Snimi boje“." />;
  // The editor appears only with the saved palette, so nothing typed before the load can be overwritten by it.
  if (!saved) return <>{title}{error ? <p className="error" role="alert">{error}</p> : <p className="empty-inline">Učitavanje…</p>}</>;

  return <>
    {title}
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    <div className="panel theme-toolbar">
      <label>Paleta<select value={presetName} onChange={(event) => choosePreset(event.target.value)} disabled={!saved}>
        {presets.map((preset) => <option key={preset.name} value={preset.name}>{preset.label}</option>)}
        {presetName === customThemeName && <option value={customThemeName}>Prilagođena</option>}
      </select></label>
      <div className="theme-actions">
        <button type="button" disabled={busy || !dirty} onClick={() => void save()}>{busy ? "Snimanje…" : "Snimi boje"}</button>
        <button type="button" className="secondary" disabled={busy || !dirty} onClick={discard}>Odbaci izmjene</button>
        <button type="button" className="secondary" disabled={busy || !saved || (sameColors(saved.colors, factoryColors) && !dirty)}
          onClick={() => void reset()}>Vrati na tvorničke postavke</button>
      </div>
    </div>
    {warnings.length > 0 && <div className="theme-warnings" role="status"><strong>Upozorenje o čitljivosti</strong>
      <ul>{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div>}
    <div className="theme-layout">
      <section className="theme-grid" aria-label="Boje">
        {colorFields.map((field) => {
          const text = hexText[field.key] ?? draft[field.key];
          const invalid = !isHexColor(text);
          return <div key={field.key} className={`theme-card${draft[field.key] !== saved?.colors[field.key] ? " changed" : ""}`}>
            <span className="theme-swatch" style={{ background: draft[field.key] }} aria-hidden="true" />
            <div className="theme-card-body">
              <strong>{field.label}</strong><small>{field.hint}</small>
              <div className="theme-inputs">
                <input type="color" aria-label={`${field.label} — odabir boje`} value={draft[field.key]}
                  onChange={(event) => setColor(field.key, event.target.value)} />
                <input type="text" aria-label={`${field.label} — heks vrijednost`} value={text} maxLength={7}
                  aria-invalid={invalid} className={invalid ? "invalid" : ""} onChange={(event) => setColor(field.key, event.target.value.trim())} />
              </div>
              {invalid && <small className="field-error">Oblik #RRGGBB</small>}
            </div>
          </div>;
        })}
      </section>
      <section className="theme-preview" aria-label="Pregled" style={previewStyle(draft)}>
        <div className="preview-header">ZEV platforma <span>admin@zev</span></div>
        <div className="preview-body">
          <nav className="preview-menu"><span className="active">Pregled</span><span>Finansije</span><span>Registar</span></nav>
          <div className="preview-content">
            <div className="preview-card"><strong>Kartica</strong><p>Sporedni tekst i opis.</p>
              <span className="preview-accent">Istaknuto</span>
              <div><button type="button" tabIndex={-1}>Primarno dugme</button></div></div>
          </div>
        </div>
      </section>
    </div>
  </>;
}
