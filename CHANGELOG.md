# Changelog

Sve značajne izmjene ZEV Platforme. Ranija historija (faze do 4) je u `CLOUD_HANDOFF_STATUS.md` i git logu.

## [v2.3] - 2026-10-10

### Dodano (Added)
- Semantički audit promjena prava: `AuditLog.Action` za `AccessOverride` je `PermissionGranted`, `PermissionDenied`
  ili `PermissionRevoked` (umjesto `Added`/`Modified`), uz autora, vrijeme, korelaciju i razlog u JSON-u.
- Razlog opoziva (`AccessOverride.RevokeReason`): reset na default u matrici i zamjena postojećeg izuzetka čuvaju
  razlog iz zahtjeva; `GET /access/overrides` vraća `revokeReason`.
- Web (lista ručnih izuzetaka u `AccessControlPage`): polje „Razlog opoziva“, razlog prikazan uz status „Opozvano“.
- Testovi `AccessAuditTests` (5): audit događaji i razlozi, zaštita od eskalacije (403), zaštita vlastitih prava i
  administratora organizacije na svim rutama, obavezan razlog opoziva, append-only pravilo za izuzetke.

### Ispravljeno (Fixed)
- `POST /api/zevs/{zevId}/access/overrides` i `POST .../{id}/revoke`: korisnik (osim sistemskog administratora) više ne
  može mijenjati ni opozivati vlastita prava (404, kao i matrica).
- `POST .../{id}/revoke` traži razlog (`{ "reason": "..." }`, do 500 znakova, inače 400).
- Radni prostor sistemskog administratora: nova stavka menija „Pristupi i veze naloga“ ponovo otvara
  `AccessControlPage` (verifikacija veze nalog–osoba, izvedeni vlasnici, ručni izuzeci i opoziv), koji od `970dd2a`
  nije bio dostupan; „Uloge i pristupi“ i dalje otvara tabelarnu matricu (`ZevPermissionPage`).
- `AccessOverride` je append-only: dozvoljen je samo jednokratni opoziv (`RevokedUtc`, `RevokedByUserId`,
  `RevokeReason`); izmjena ostalih polja, ponovni opoziv ili brisanje se odbijaju.

### Baza / Migracije
- `20261010182929_AccessOverrideRevokeReason`: kolona `AccessOverrides.RevokeReason` (nvarchar(500)),
  check `CK_AccessOverrides_RevokeReason` (razlog samo uz opoziv).

### Otvorene odluke (Open Decisions)
- „Pristupi i veze naloga“ i dalje sadrži i matricu prava (ista kao „Uloge i pristupi“); razdvajanje u PR 5 po potrebi.
- `/revoke` opoziva samo grube ključeve (`zev.read`, `finance.manage`, …); detaljna prava se vraćaju kroz matricu.

## [v2.2] - 2026-10-10

### Dodano (Added)
- Storno ručne uplate: `POST /api/zevs/{zevId}/payments/{paymentId}/reverse` s tijelom `{ "reason": "..." }`.
  - Zahtijeva pravo `Payments.Reverse` (bez prava 403; bez pristupa ZEV-u 404) i obavezan razlog (do 500 znakova, inače 400).
  - Uplata dobija `Status = Reversed`, datum storna, razlog i autora (`ReversalReason`, `ReversedByUserId`).
  - Aktivne raspodjele se oslobađaju negativnim zapisima s istim razlogom (zaduženja ponovo postaju otvorena); već oslobođene se preskaču.
  - Knjiga fonda dobija `FundTransaction` tipa `Reversal` (−iznos, veza na originalnu uplatu), pa saldo fonda ostaje tačan.
  - Ponovljen zahtjev s istim razlogom je idempotentan (`alreadyExists: true`); drugačiji razlog ili bankovna uplata → 409 (bankovne uplate se storniraju povratom u izvodu).
  - Audit: promjena uplate i nove raspodjele se automatski upisuju u `AuditLog` (autor, vrijeme, razlog u before/after JSON-u).
- Testovi: `ManualPaymentReversalApiTests` (uspjeh, 403 bez prava / uz DENY, 404 bez pristupa, 400 bez razloga) i 4 servisna testa u `PaymentTests`.

### Ispravljeno (Fixed)
- Uplatnica po zaduženju oduzima kreditne korekcije i storno (`c050cc5`).

### Baza / Migracije
- `20261010181657_ManualPaymentReversal`: kolone `Payments.ReversalReason` (nvarchar(500)) i `Payments.ReversedByUserId`,
  check `CK_Payments_Reversal` (razlog samo uz status `Reversed`). Append-only pravilo dozvoljava ova polja samo
  jednom, zajedno s prelazom `Posted → Reversed`.

### Otvorene odluke (Open Decisions)
- Datum storna je dan izvršenja (UTC); retroaktivni datum storna nije podržan.
- Web UI za storno ručne uplate još ne postoji (samo API).
- Djelimični storno/povrat uplate nije podržan.
