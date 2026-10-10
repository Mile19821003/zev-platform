import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { callApi, errorText, Page } from "../api";
import { Pager, SectionTitle } from "./shared";
import { announcementScopeLabels, ballotKindLabels, ballotModeLabels, ballotStatusLabels, documentTypeLabels, label,
  membershipRoleLabels, visibilityLabels } from "../uiLabels.ts";
import { useChangeRevision } from "../useChangeRevision.ts";

type ZevOption = { id: string; code: string; name: string };
type Announcement = { id: string; title: string; content: string; scope: string; publishedFromUtc: string };
type Document = { id: string; title: string; type: string; visibility: string; createdUtc: string };
type Ballot = { id: string; title: string; question: string; kind: string; mode: string; status: string;
  opensUtc: string; closesUtc: string; quorumMet: boolean | null; approved: boolean | null;
  yesWeight: number | null; noWeight: number | null };
type Unit = { id: string; zevId: string; code: string };

export default function CommunityPage({ token, zevs, role, permissions }: {
  token: string; zevs: ZevOption[]; role: "manager" | "representative" | "owner"; permissions?: string[];
}) {
  const allowed = (key: string) => !permissions || permissions.includes(key);
  const [zevId, setZevId] = useState(zevs[0]?.id ?? "");
  const [announcements, setAnnouncements] = useState<Page<Announcement>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [documents, setDocuments] = useState<Page<Document>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [ballots, setBallots] = useState<Page<Ballot>>({ page: 1, pageSize: 20, totalCount: 0, items: [] });
  const [units, setUnits] = useState<Unit[]>([]);
  const [page, setPage] = useState(1);
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [scope, setScope] = useState("Zev");
  const [targetRole, setTargetRole] = useState("Owner");
  const [buildingId, setBuildingId] = useState("");
  const [entranceId, setEntranceId] = useState("");
  const [publishedFrom, setPublishedFrom] = useState("");
  const [expires, setExpires] = useState("");
  const [documentTitle, setDocumentTitle] = useState("");
  const [documentType, setDocumentType] = useState("Other");
  const [visibility, setVisibility] = useState("ZevMembers");
  const [file, setFile] = useState<File | null>(null);
  const [ballotTitle, setBallotTitle] = useState("");
  const [question, setQuestion] = useState("");
  const [kind, setKind] = useState("Survey");
  const [mode, setMode] = useState("OneUnitOneVote");
  const [weightBasis, setWeightBasis] = useState("UnitArea");
  const [denominator, setDenominator] = useState("Participating");
  const [quorum, setQuorum] = useState("");
  const [approval, setApproval] = useState("");
  const [opens, setOpens] = useState("");
  const [closes, setCloses] = useState("");
  const [voteUnitId, setVoteUnitId] = useState("");
  const [choice, setChoice] = useState("Yes");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const requestId = useRef(0);
  const revision = useChangeRevision(`zev:${zevId}:community`, `zev:${zevId}:ballots`);

  const refresh = useCallback(async () => {
    const currentRequest = ++requestId.current;
    if (!zevId) return;
    try {
      const prefix = `/api/zevs/${zevId}`;
      const [announcementData, documentData, ballotData, unitData] = await Promise.all([
        allowed("Community.View") ? callApi<Page<Announcement>>(`${prefix}/community/announcements?page=${page}`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
        allowed("Documents.View") ? callApi<Page<Document>>(`${prefix}/community/documents`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
        allowed("Voting.View") ? callApi<Page<Ballot>>(`${prefix}/ballots`, {}, token)
          : Promise.resolve({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
        callApi<Unit[]>("/api/me/property-units", {}, token),
      ]);
      if (currentRequest === requestId.current) {
        if (page > 1 && announcementData.items.length === 0) { setPage(Math.max(1, Math.ceil(announcementData.totalCount / 20))); return; }
        setAnnouncements(announcementData); setDocuments(documentData); setBallots(ballotData);
        setUnits(unitData.filter((unit) => unit.zevId === zevId)); setError("");
      }
    } catch (problem) { if (currentRequest === requestId.current) setError(errorText(problem)); }
  }, [zevId, page, token, permissions]);

  useEffect(() => { void refresh(); return () => { requestId.current++; }; }, [refresh, revision]);

  async function submit(path: string, body: object, message: string) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      await callApi(`/api/zevs/${zevId}/${path}`, { method: "POST", body: JSON.stringify(body) }, token);
      setNotice(message);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function publish(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("community/announcements", { title, content, scope,
      targetRole: scope === "Role" ? targetRole : null, buildingId: buildingId || null,
      entranceId: entranceId || null, publishedFromUtc: new Date(publishedFrom).toISOString(),
      expiresUtc: expires ? new Date(expires).toISOString() : null, fileIds: [] }, "Objava je sačuvana.");
  }

  async function addDocument(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current) return;
    if (!file || file.size > 5_000_000) { setError("Odaberite datoteku do 5 MB."); return; }
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const contentBase64 = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(file);
      });
      const uploaded = await callApi<{ id: string }>(`/api/zevs/${zevId}/files`, { method: "POST",
        body: JSON.stringify({ fileName: file.name, contentType: file.type, contentBase64 }) }, token);
      await callApi(`/api/zevs/${zevId}/community/documents`, { method: "POST",
        body: JSON.stringify({ title: documentTitle, type: documentType, visibility,
          fileId: uploaded.id, personId: null }) }, token);
      setNotice("Dokument je sačuvan."); setFile(null);
    } catch (problem) { setError(errorText(problem)); }
    finally { pending.current = false; setBusy(false); }
  }

  function createBallot(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void submit("ballots", { title: ballotTitle, question, kind, mode,
      weightBasis, approvalDenominator: denominator, quorumPercent: Number(quorum),
      approvalPercent: Number(approval), opensUtc: new Date(opens).toISOString(),
      closesUtc: new Date(closes).toISOString() }, "Glasanje je kreirano kao nacrt.");
  }

  function vote(ballot: Ballot) {
    void submit(`ballots/${ballot.id}/votes`, { propertyUnitId: voteUnitId, choice }, "Glas je evidentiran.");
  }

  async function download(document: Document) {
    try {
      const response = await fetch(`${import.meta.env.VITE_API_URL ?? "http://localhost:8080"}/api/zevs/${zevId}/community/documents/${document.id}/download`,
        { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error();
      const url = URL.createObjectURL(await response.blob());
      window.open(url, "_blank", "noopener,noreferrer"); setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch { setError("Dokument nije moguće otvoriti."); }
  }

  return <>
    <SectionTitle eyebrow={role === "owner" ? null : undefined} title="Zajednica" description="Oglasna tabla, dokumenti, ankete i formalna glasanja." />
    <div className="panel finance-selector"><label>ZEV<select value={zevId} onChange={(event) => setZevId(event.target.value)}>
      {zevs.map((zev) => <option key={zev.id} value={zev.id}>{zev.name} ({zev.code})</option>)}
    </select></label></div>
    {error && <p className="error" role="alert">{error}</p>}{notice && <p className="notice" role="status">{notice}</p>}
    {zevId && <>{role !== "owner" && <div className="finance-grid">
      {allowed("Announcements.Create") && <section className="panel"><h2>Nova objava</h2><form className="admin-form" onSubmit={publish}>
        <label>Naslov<input required maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} /></label>
        <label>Sadržaj<textarea required maxLength={10000} value={content} onChange={(event) => setContent(event.target.value)} /></label>
        <label>Publika<select value={scope} onChange={(event) => setScope(event.target.value)}>
          <option value="Zev">Cijeli ZEV</option><option value="Building">Zgrada</option>
          <option value="Entrance">Ulaz</option><option value="Role">Uloga</option></select></label>
        {(scope === "Building" || scope === "Entrance") && <label>ID zgrade<input required value={buildingId} onChange={(event) => setBuildingId(event.target.value)} /></label>}
        {scope === "Entrance" && <label>ID ulaza<input required value={entranceId} onChange={(event) => setEntranceId(event.target.value)} /></label>}
        {scope === "Role" && <label>Uloga<select value={targetRole} onChange={(event) => setTargetRole(event.target.value)}>
          {['Owner', 'Manager', 'Accountant', 'BuildingRepresentative'].map((item) => <option key={item} value={item}>{label(membershipRoleLabels, item)}</option>)}</select></label>}
        <label>Objava od<input required type="datetime-local" value={publishedFrom} onChange={(event) => setPublishedFrom(event.target.value)} /></label>
        <label>Ističe<input type="datetime-local" value={expires} onChange={(event) => setExpires(event.target.value)} /></label>
        <button disabled={busy}>Objavi</button></form></section>}
      {role === "manager" && <>{allowed("Documents.Upload") && <section className="panel"><h2>Novi dokument</h2>
        <form className="admin-form" onSubmit={addDocument}>
          <label>Naslov<input required maxLength={200} value={documentTitle} onChange={(event) => setDocumentTitle(event.target.value)} /></label>
          <label>Tip<select value={documentType} onChange={(event) => setDocumentType(event.target.value)}>
            {['Contract', 'Policy', 'Statute', 'Minutes', 'Financial', 'Legal', 'Other'].map((item) => <option key={item} value={item}>{label(documentTypeLabels, item)}</option>)}</select></label>
          <label>Pristup<select value={visibility} onChange={(event) => setVisibility(event.target.value)}>
            <option value="ZevMembers">Članovi ZEV-a</option><option value="ManagersOnly">Samo upravnik</option></select></label>
          <label>Datoteka<input required type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(event) => setFile(event.target.files?.[0] ?? null)} /></label>
          <button disabled={busy}>Dodaj dokument</button></form></section>}
      {allowed("Voting.Create") && <section className="panel"><h2>Novo glasanje</h2><form className="admin-form" onSubmit={createBallot}>
        <label>Naslov<input required maxLength={200} value={ballotTitle} onChange={(event) => setBallotTitle(event.target.value)} /></label>
        <label>Pitanje<textarea required maxLength={4000} value={question} onChange={(event) => setQuestion(event.target.value)} /></label>
        <label>Vrsta<select value={kind} onChange={(event) => setKind(event.target.value)}><option value="Survey">Anketa</option><option value="Formal">Formalno</option></select></label>
        <label>Način<select value={mode} onChange={(event) => setMode(event.target.value)}><option value="OneUnitOneVote">Jedan stan = jedan glas</option><option value="Weighted">Ponderisano</option></select></label>
        {mode === "Weighted" && <label>Osnova pondera<select value={weightBasis} onChange={(event) => setWeightBasis(event.target.value)}><option value="UnitArea">Površina</option><option value="UnitCoefficient">Koeficijent</option></select></label>}
        <label>Osnova većine<select value={denominator} onChange={(event) => setDenominator(event.target.value)}><option value="Participating">Učesnici</option><option value="Eligible">Svi s pravom glasa</option><option value="YesNo">Za/Protiv</option></select></label>
        <label>Kvorum (%)<input required type="number" min="0" max="100" step="0.0001" value={quorum} onChange={(event) => setQuorum(event.target.value)} /></label>
        <label>Potrebna većina (%)<input required type="number" min="0" max="100" step="0.0001" value={approval} onChange={(event) => setApproval(event.target.value)} /></label>
        <label>Početak<input required type="datetime-local" value={opens} onChange={(event) => setOpens(event.target.value)} /></label>
        <label>Kraj<input required type="datetime-local" value={closes} onChange={(event) => setCloses(event.target.value)} /></label>
        <button disabled={busy}>Kreiraj nacrt</button></form></section>}</>}
    </div>}
    <section className="panel table-panel"><h2>Oglasna tabla</h2>{announcements.items.map((item) =>
      <article className="announcement-card" key={item.id}><h3>{item.title}</h3><p>{item.content}</p>
        <small>{item.publishedFromUtc.slice(0, 10)} · {label(announcementScopeLabels, item.scope)}</small></article>)}
      {announcements.items.length === 0 && <p className="empty-inline">Nema objava.</p>}
      <Pager {...announcements} onPage={setPage} /></section>
    <section className="panel table-panel"><h2>Dokumenti</h2><div className="table-scroll"><table><thead><tr>
      <th>Naslov</th><th>Tip</th><th>Pristup</th><th>Radnja</th></tr></thead><tbody>
      {documents.items.map((item) => <tr key={item.id}><td>{item.title}</td><td>{label(documentTypeLabels, item.type)}</td>
        <td>{label(visibilityLabels, item.visibility)}</td><td className="actions">{allowed("Documents.Download") && <button type="button" onClick={() => download(item)}>Otvori</button>}</td></tr>)}
      </tbody></table></div></section>
    <section className="panel table-panel"><h2>Glasanja</h2>{role === "owner" && <div className="vote-picker">
      <label>Nekretnina<select value={voteUnitId} onChange={(event) => setVoteUnitId(event.target.value)}>
        <option value="">Odaberite</option>{units.map((item) => <option key={item.id} value={item.id}>{item.code}</option>)}</select></label>
      <label>Glas<select value={choice} onChange={(event) => setChoice(event.target.value)}>
        <option value="Yes">Za</option><option value="No">Protiv</option><option value="Abstain">Suzdržan</option></select></label></div>}
      {ballots.items.map((item) => <article className="announcement-card" key={item.id}>
        <h3>{item.title}</h3><p>{item.question}</p><small>{label(ballotKindLabels, item.kind)} · {label(ballotModeLabels, item.mode)} · {label(ballotStatusLabels, item.status)}</small>
        {item.status === "Closed" && <p>Za: {item.yesWeight} · Protiv: {item.noWeight} · Kvorum: {item.quorumMet ? "da" : "ne"} · Usvojeno: {item.approved ? "da" : "ne"}</p>}
        {role === "manager" && allowed("Voting.Manage") && item.status === "Draft" && <button onClick={() => submit(`ballots/${item.id}/open`, {}, "Glasanje je otvoreno.")}>Otvori</button>}
        {role === "manager" && allowed("Voting.Manage") && item.status === "Open" && <button onClick={() => submit(`ballots/${item.id}/close`, {}, "Glasanje je zaključano.")}>Zaključi</button>}
        {role === "owner" && item.status === "Open" && <button disabled={busy || !voteUnitId} onClick={() => vote(item)}>Glasaj</button>}
      </article>)}
      {ballots.items.length === 0 && <p className="empty-inline">Nema glasanja.</p>}
    </section></>}
  </>;
}
