import { useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { formatDate } from "../displaySettings.ts";
import { AppVersion, sortVersions, versionNoteChanged, versionStatusLabel } from "../systemOpsUi.ts";
import { SectionTitle } from "./shared";

// Administration → "Istorija verzija": installed releases with an editable administrator note per version.
export default function VersionHistoryPage({ token }: { token: string }) {
  const [versions, setVersions] = useState<AppVersion[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    let current = true;
    callApi<AppVersion[]>("/api/system/versions", {}, token)
      .then((result) => { if (current) { setVersions(sortVersions(result)); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, revision]);

  async function save(version: AppVersion) {
    setBusy(version.version); setError(""); setNotice("");
    try {
      await callApi(`/api/system/versions/${encodeURIComponent(version.version)}/notes`, { method: "PUT",
        body: JSON.stringify({ adminNotes: drafts[version.version] ?? "" }) }, token);
      setDrafts((current) => { const next = { ...current }; delete next[version.version]; return next; });
      setNotice(`Bilješka za ${version.version} je snimljena.`);
      setRevision((value) => value + 1);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(""); }
  }

  return <>
    <SectionTitle title="Istorija verzija" description="Instalirane verzije aplikacije (prema CHANGELOG.md) i bilješke administratora." />
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    <section className="panel"><div className="table-scroll"><table className="versions-table">
      <thead><tr><th>Verzija</th><th>Status</th><th>Naslov</th><th>Instalirano</th><th>Notes</th><th>Akcija</th></tr></thead>
      <tbody>{versions.map((version) => <tr key={version.version}>
        <td><strong>{version.version}</strong></td><td><span className="badge installed">{versionStatusLabel(version.status)}</span></td>
        <td>{version.title}</td><td>{formatDate(version.installedAt)}<br /><small>{version.installedBy}</small></td>
        <td><textarea rows={2} maxLength={2000} aria-label={`Bilješka za ${version.version}`}
          value={drafts[version.version] ?? version.adminNotes ?? ""}
          onChange={(event) => setDrafts((current) => ({ ...current, [version.version]: event.target.value }))} /></td>
        <td className="actions"><button type="button" disabled={busy !== "" || !versionNoteChanged(version, drafts[version.version])}
          onClick={() => void save(version)}>{busy === version.version ? "Snimanje…" : "Snimi note"}</button></td>
      </tr>)}</tbody></table></div>
      {versions.length === 0 && !error && <p className="empty-inline">Učitavanje…</p>}
    </section>
  </>;
}
