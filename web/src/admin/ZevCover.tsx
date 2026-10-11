import { ChangeEvent, useEffect, useState } from "react";
import { cachedZevImage, imageAccept, loadZevImage, validateImageFile } from "../zevImage.ts";
import { NavIcon } from "./WorkspaceNav";

// Top of a community card: the building photo (16:9, cover) or a neutral placeholder with the building icon.
export function ZevCoverView({ src, name }: { src: string | null; name: string }) {
  return <div className={`zev-cover${src ? "" : " placeholder"}`}>
    {src ? <img src={src} alt={`Fotografija zgrade: ${name}`} loading="lazy" />
      : <span className="zev-cover-icon" role="img" aria-label="Zgrada bez fotografije"><NavIcon name="building" size={40} /></span>}
  </div>;
}

export function useZevImage(token: string | null | undefined, url: string | null | undefined): string | null {
  const [src, setSrc] = useState<string | null>(() => cachedZevImage(url)?.src ?? null);
  useEffect(() => {
    if (!url || !token) { setSrc(null); return; }
    let current = true;
    setSrc(cachedZevImage(url)?.src ?? null);
    void loadZevImage(url, token).then((loaded) => { if (current) setSrc(loaded.src); });
    return () => { current = false; };
  }, [url, token]);
  return src;
}

export default function ZevCover({ token, imageUrl, name }: { token: string | null; imageUrl?: string | null; name: string }) {
  return <ZevCoverView src={useZevImage(token, imageUrl)} name={name} />;
}

export type ImageDraft = { file: File | null; remove: boolean };
export const emptyImageDraft: ImageDraft = { file: null, remove: false };

// Photo picker for the ZEV form: preview of the chosen file (or the saved photo) and "Ukloni sliku".
// Nothing is uploaded here; the form sends the draft after the record itself is saved.
export function ImageUploadField({ label, token, currentUrl, name, draft, onChange }: {
  label: string; token: string; currentUrl: string | null; name: string; draft: ImageDraft;
  onChange: (draft: ImageDraft) => void;
}) {
  const [error, setError] = useState("");
  const [preview, setPreview] = useState<string | null>(null);
  const saved = useZevImage(token, draft.remove ? null : currentUrl);
  useEffect(() => {
    if (!draft.file) { setPreview(null); return; }
    const url = URL.createObjectURL(draft.file);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [draft.file]);

  function choose(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    const problem = validateImageFile(file);
    setError(problem ?? "");
    if (!problem) onChange({ file, remove: false });
  }

  const shown = preview ?? (draft.remove ? null : saved);
  return <div className="image-upload-field">
    <span className="image-upload-label">{label}</span>
    <ZevCoverView src={shown} name={name || "novi ZEV"} />
    <div className="image-upload-actions">
      <label className="file-button">{shown ? "Zamijeni sliku" : "Dodaj sliku"}
        <input type="file" accept={imageAccept} onChange={choose} aria-label={label} /></label>
      {(draft.file || (currentUrl && !draft.remove)) && <button type="button" className="danger"
        onClick={() => { setError(""); onChange({ file: null, remove: Boolean(currentUrl) }); }}>Ukloni sliku</button>}
    </div>
    <small>JPG, PNG ili WEBP, najviše 5 MB.{draft.file ? " Slika se šalje pri snimanju." : ""}
      {draft.remove ? " Slika će biti uklonjena pri snimanju." : ""}</small>
    {error && <small className="field-error" role="alert">{error}</small>}
  </div>;
}
