# Changelog

Sve značajne izmjene ZEV Platforme. Ranija historija (faze do 4) je u `CLOUD_HANDOFF_STATUS.md` i git logu.

## [v2.6] - 2026-10-10

### Dodano (Added)
- Pravilo „403 ili 404“ za rute unutar ZEV-a, dokumentovano u `ACCESS_CONTROL_AND_PRIVACY.md`; helperi
  `TenantAccess.RequirePermissionAsync` / `RequireAllPermissionsAsync` / `DeniedAsync`.
- Web „Uplate i izvodi“ → „Bankovni račun“: dugme „Prikaži“ po računu i sekcija „Stanje računa i usklađivanje“
  (`BankAccountBalanceCard.tsx`, logika u `bankBalanceUi.ts`): stanje banke iz izvoda (iskazani saldo i datum,
  prilivi/odlivi, izvedeni saldo i neslaganje), stanje knjige fonda po izvoru, razlaganje razlike, status
  (usklađeno / razlika objašnjena / prekid u nizu izvoda / neobjašnjena razlika) i upozorenja o prekidima.
  Vidljivo uz `BankAccounts.View`.
- Tabela „Uvezeni izvodi“: kolone Period, Početni saldo, Završni saldo; pregled izvoda prikazuje saldo i period.
- `zev-platform.slnx` u korijenu (API + testovi): `dotnet build` / `dotnet test` rade iz korijena (ranije MSB1003).
- `MILESTONE_STATUS_v2.6.md`: stanje specifikacije, otvorene odluke i backlog za tim.
- Testovi: `AuthorizationPolicyTests` (2: 403 unutar ZEV-a, 404 za tuđi/nepostojeći ZEV),
  `web/tests/bankBalance.test.mjs` (3).

### Ispravljeno (Fixed)
- Rute unutar ZEV-a vraćaju **403** kada korisnik može čitati ZEV, a nema traženo pravo (ranije uglavnom 404,
  a samo 5 ruta ručnih uplata 403). Tuđi ili nepostojeći ZEV i dalje **404**. Pravo se provjerava prije
  postojanja resursa. Obuhvaćeno ~110 provjera u 14 endpoint fajlova (plaćanja, finansije, računi, storno/korekcije,
  rashodi, izvještaji, pravni predmeti, održavanje, registar, zajednica, glasanje, pristupi).
- `/access/overrides` i `/revoke`: delegiranje grubog prava koje dodjeljivač nema vraća 403 (ranije 404).
- Bez promjene (namjerno 404): tuđi lični resursi (kartica, račun, uplatnica, `PersonOnly`/`PropertyUnit`
  dokument, fajl, tuđa prijava kvara), zaštićene mete upravljanja pravima, rute organizacije i `/api/me`.
- Testovi koji su očekivali 404 za nedostajuće pravo ažurirani na 403 (12 provjera u 4 test fajla).

### Otvorene odluke (Open Decisions)
- Web `formatMoney` koristi `Intl` s lokalom `bs-BA`; preglednici bez tog lokala (npr. Edge u testnom okruženju)
  prikazuju `145.00 KM` umjesto `145,00 KM`. Postojeće ponašanje cijelog weba — za frontend polish (ručno
  formatiranje kao u mobilnoj aplikaciji).

## [v2.5] - 2026-10-10

### Dodano (Added)
- Saldo i period izvoda: `BankStatementImport.OpeningBalance`, `ClosingBalance`, `StatementFrom`, `StatementTo`.
  - CAMT.053: početni saldo `OPBD` (ili `PRCD`), završni `CLBD`, predznak iz `CdtDbtInd`, period iz `FrToDt`;
    valuta salda mora odgovarati valuti računa.
  - Interni XML: opcioni `<openingBalance>`, `<closingBalance>`, `<from>`, `<to>` pod korijenom.
  - CSV/TXT nemaju standardna polja salda — vrijednosti ostaju prazne (ništa se ne izvodi iz transakcija).
  - Prikaz u pregledu (`POST .../statements/preview`) i listi izvoda (`GET .../statements`).
- `GET /api/zevs/{zevId}/payments/accounts/{accountId}/balance` (pravo `BankAccounts.View`, inače 404):
  - **Banka** (samo iz izvoda): posljednji iskazani završni saldo i datum (`balanceSource` = `Statement` ili
    `NotProvided`), promet (prilivi, odlivi), izvedeni saldo = početni saldo najstarijeg izvoda + sav promet
    (samo ako je početni saldo poznat), lista izvoda i prekidi kontinuiteta (završni ≠ sljedeći početni).
  - **Fond** (samo iz knjige fonda, valuta računa): saldo i sastav — uplate s ovog računa, s drugih računa istog
    fonda, ručne uplate, rashodi, ostalo.
  - **Usklađivanje**: razlika promet banke − saldo fonda razložena na bankovne odlive koji nisu u fondu, ručne
    uplate i rashode koji nisu u banci i druge račune fonda; `unexplained` treba biti 0.
- Testovi: `BankAccountBalanceTests` (5: parseri, snimanje i odbijanje izvoda, razlaganje razlike, prekid
  kontinuiteta, izolacija po ZEV-u i prava) i `SqlBankAccountBalanceTests` (SQL Server).

### Ispravljeno (Fixed)
- Uvoz odbija izvod čiji se iskazani saldo ne slaže s transakcijama (početni + promet ≠ završni), izvod čija
  je valuta salda različita od valute računa i izvod čiji period završava prije početka.

### Baza / Migracije
- `20261010191157_BankStatementBalances`: kolone `OpeningBalance`, `ClosingBalance` (decimal(18,2)),
  `StatementFrom`, `StatementTo` (date) na `BankStatementImports`, check `CK_BankStatementImports_Period`.
  Postojeći izvodi ostaju bez salda (null).

### Otvorene odluke (Open Decisions)
- Bankovni odlivi (provizije, isplate dobavljačima) i dalje ne knjiže `FundTransaction`; izvještaj ih prikazuje
  kao „odlivi koji nisu u fondu“. Potrebna odluka da li ih vezati za rashode/knjigu fonda.
- Saldo za CSV/TXT izvode: banke ga ponekad daju u zaglavlju ili podnožju bez standarda — podrška po banci
  kad budu dostupni stvarni uzorci.
- Web prikaz salda računa (UI) još ne postoji (samo API).

## [v2.4] - 2026-10-10

### Dodano (Added)
- Matrica prava (`PermissionMatrix.tsx`): ćelije jasno razlikuju izvor prava — `✓` naslijeđeno iz uloge (zeleno),
  `✓+` ručni GRANT (plavo), `✕` ručni DENY (crveno), `□` nije dozvoljeno; tooltip navodi i šta bi uloga dala
  („uloga bi dozvolila“ / „uloga već dozvoljava“). Legenda iznad tabele.
- Nesačuvane promjene: isprekidan okvir ćelije, brojač „Nesačuvanih promjena: N“ i dugme „Odbaci nacrt“.
- Logika matrice izdvojena u `web/src/permissionMatrixUi.ts`; testovi `web/tests/permissionMatrix.test.mjs` (5).

### Ispravljeno (Fixed)
- Dva dugmeta istog ponašanja („Primijeni default uloge“, „Resetuj na default“) spojena u jedno:
  „Vrati sve na default uloge“ (u nacrt stavlja samo ključeve koji imaju ručni izuzetak).
- Kolona „Brisanje“ (nijedan modul nema akciju Delete) se više ne prikazuje; kolone se računaju iz modula,
  pa se pojavljuje automatski kad modul dobije tu akciju. Detaljni redovi koriste tačan `colSpan`.
- Klik na ćeliju: default → suprotan ručni izuzetak → nazad na default (ranije DENY → GRANT bez povratka na default).
- Snimanje šalje samo stvarne promjene (ranije i ključeve bez izmjene, što je moglo izazvati 403 provjere
  dodjeljivača za prava koja on nema); „Snimi prava“ je onemogućeno bez promjena.
- Poruka „Prava su sačuvana“ nestaje čim se nacrt ponovo mijenja; detaljni ključevi poravnati lijevo.

### Otvorene odluke (Open Decisions)
- Na mobilnom (390 px) stranica s 6+ stavki menija ima horizontalni scroll (bočni meni se ne prelama) — globalni
  raspored, postojao i prije; nije dio ove izmjene.

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
