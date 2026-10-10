# Changelog

Sve značajne izmjene ZEV Platforme. Ranija historija (faze do 4) je u `CLOUD_HANDOFF_STATUS.md` i git logu.

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
