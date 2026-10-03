import { FormEvent, useState } from "react";
import { callApi, errorText } from "./api";

type TokenResponse = { accessToken: string; expiresUtc: string };

export default function PasswordChangePage({ token, email, forced, onChanged, onCancel, onLogout }: {
  token: string; email: string; forced: boolean; onChanged: (nextToken: string) => void;
  onCancel: () => void; onLogout: () => void;
}) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (newPassword !== confirmPassword) {
      setError("Lozinke se ne podudaraju.");
      return;
    }
    setBusy(true); setError("");
    try {
      const result = await callApi<TokenResponse>("/api/me/password", {
        method: "POST", body: JSON.stringify({ currentPassword: forced ? null : currentPassword,
          newPassword, confirmPassword })
      }, token);
      setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
      onChanged(result.accessToken);
    } catch (problem) { setError(errorText(problem)); }
    finally { setBusy(false); }
  }

  return <main className="login-page"><section className="login-intro">
    <div className="brand-mark">Z</div><p className="eyebrow">ZEV PLATFORMA</p>
    <h1>{forced ? "Promjena lozinke je obavezna." : "Moj profil"}</h1>
    <p>{forced ? "Prije nastavka rada postavite novu lozinku." : "Promijenite lozinku svog naloga."}</p>
  </section><section className="login-card">
    <h2>Promijeni lozinku</h2><p>{email}</p>
    <form onSubmit={submit}>
      {!forced && <><label htmlFor="current-password">Trenutna lozinka</label>
        <input id="current-password" type="password" autoComplete="current-password"
          required value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} /></>}
      <label htmlFor="new-password">Nova lozinka</label>
      <input id="new-password" type="password" autoComplete="new-password"
        required value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
      <label htmlFor="confirm-password">Potvrdi novu lozinku</label>
      <input id="confirm-password" type="password" autoComplete="new-password"
        required value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
      <p>Najmanje 12 znakova, veliko i malo slovo, broj i specijalni znak. Server može zahtijevati veću dužinu.</p>
      {error && <p className="error" role="alert">{error}</p>}
      <button type="submit" disabled={busy}>{busy ? "Čuvanje…" : "Promijeni lozinku"}</button>
      <button type="button" className="profile-secondary" onClick={forced ? onLogout : onCancel}>
        {forced ? "Odjavi se" : "Otkaži"}</button>
    </form>
  </section></main>;
}
