# ZEV Platforma — stanje projekta, milestone v2.6 (10.10.2026.)

Pregled za tim: šta je iz specifikacije „Access Control matrica + trajni Invoice model“ završeno i pokriveno
testovima, te šta ostaje kao otvorena poslovna odluka ili backlog. Detalji izmjena po verzijama su u
`CHANGELOG.md` (v2.2–v2.6), pravila pristupa u `ACCESS_CONTROL_AND_PRIVACY.md`.

## Stanje koda i testova

- Grana `main`, posljednji commit milestone-a: `4c49103` (PR 7); radno stablo čisto.
- `dotnet build` / `dotnet test` rade iz korijena repozitorija (`zev-platform.slnx`).
- Backend: build 0 upozorenja / 0 grešaka; **220/220** testova, uključujući SQL integracijske testove na
  lokalnom SQL Serveru (`ZEV_TEST_SQL_CONNECTION`). Na početku ovog ciklusa: 200.
- Web: `npm run build --prefix web` PASS; **72/72** testova (ranije 64).
- Mobilna aplikacija: Jest **33/33**, typecheck PASS, `expo export` Android/iOS PASS.

### PR-ovi ovog ciklusa

| PR | Sadržaj | Commit |
|---|---|---|
| Ispravka | Uplatnica po zaduženju oduzima kreditne korekcije i storno | `c050cc5` |
| PR 3 | Storno ručne uplate (`POST .../payments/{id}/reverse`) | `37506ee` |
| PR 4 | Semantički audit prava, razlog opoziva, zaštita ruta za prava | `8bd988b` |
| PR 5 | Matrica prava: ✓ / ✓+ / ✕ / □, jedno reset dugme, bez prazne kolone Brisanje | `53e3e05` |
| PR 2 | Saldo izvoda i stanje banke naspram fonda (`GET .../accounts/{id}/balance`) | `7ea6d62` |
| PR 6 | Pravilo 403/404 za rute unutar ZEV-a | `eeee127` |
| PR 7 | Web prikaz stanja računa i usklađivanja | `4c49103` |

## Dio 1 — Access Control

| Zahtjev | Stanje | Dokaz (testovi) |
|---|---|---|
| `AccessOverride` / `Permission` / `RolePermission`; centralni servis; default uloge + GRANT − DENY; DENY ima prednost | ✅ | `EffectiveAccessService`; `TenantAccessTests`, `GranularDenyOverridesRoleAndGrantOnlyInsideSelectedZev`, `ExplicitDenyBeatsOwnerAndGrantExpires` |
| Backend provjerava konkretna prava; 403 za nedostajuće pravo unutar ZEV-a, 404 za tuđi ZEV | ✅ (PR 6) | `AuthorizationPolicyTests`, `ManagerPaymentOverridesEnforceActionsAndZevBoundary` |
| Zaštita opsega dodjeljivača (bez eskalacije, bez vlastitih prava, bez organizacijskog administratora) | ✅ (PR 4) | `AccessAuditTests` |
| Audit `PermissionGranted` / `PermissionDenied` / `PermissionRevoked` s razlogom i autorom | ✅ (PR 4) | `AccessAuditTests` |
| Privatnost vlasnika; pristup izveden iz verifikovanog vlasništva | ✅ | `OwnerFinanceShowsOnlyLinkedPersonAndRejectsManagerReports`, `OwnerCannotSeeAnotherPersonsPrivateDocument`, `OwnerAccessRequiresVerifiedLinkAndExpiresWithOwnership` |
| Tabelarna matrica: kolone, izvor prava, expander pod-akcija, reset, kopiranje, snimanje jednim pozivom | ✅ (PR 5) | `web/tests/permissionMatrix.test.mjs`; browser provjera 17/17 |
| Radni prostor: aktivni ZEV i meni prema efektivnim pravima bez F5 | ✅ | `web/tests/dataSync.test.mjs` (osvježavanje na izmjenu i fokus; bez server push-a) |
| Zajednički agregati za vlasnike (`ZEV_SHARED`) | ⏳ otvorena odluka | — |

## Dio 2 — Finansijski core i trajni račun

| Zahtjev | Stanje | Dokaz (testovi) |
|---|---|---|
| Trajni `Invoice` / `InvoiceLine` sa snapshotima; promjena tarife ne mijenja izdat račun | ✅ | `PostedInvoicePdfRemainsStableAfterMasterChangesAndSlipUsesOutstandingAmount` (PDF bajt-identičan), `InvoiceLifecycleTests.PostedInvoiceSnapshotAndCorrectionDocumentAreImmutable` |
| Jedno zaduženje najviše na jednom računu | ✅ | jedinstveni indeks `IX_InvoiceLines_ChargeId` |
| Konkurentno sigurna numeracija | ✅ | `SqlBillingConcurrencyTests`, `InvoiceNumberProviderFormatsConfiguredPrefix` |
| Storno i korektivni dokumenti, append-only | ✅ | `InvoiceLifecycleTests` (12), `SqlInvoiceLifecycleConcurrencyTests` |
| Uplatnica nudi preostali dug (i nakon korekcije) | ✅ (+ `c050cc5`) | `…SlipUsesOutstandingAmount`, `InvoiceLifecycleApiEnforcesPermissionsAndOwnerPdfPrivacy` |
| Stanje fonda odvojeno od stanja bankovnog računa, s usklađivanjem | ✅ (PR 2, PR 7) | `BankAccountBalanceTests`, `SqlBankAccountBalanceTests`, `web/tests/bankBalance.test.mjs` |
| Ručne uplate s razlogom i auditom; storno ručne uplate | ✅ (PR 3) | `ManualPaymentReversalApiTests`, `PaymentTests.ManualPaymentReversal*` |

## Otvorene poslovne odluke

1. **Agregati za vlasnike (`ZEV_SHARED`)** — šta se objavljuje vlasnicima i ko to odobrava.
2. **Bankovni odlivi** (provizije, isplate dobavljačima) se ne knjiže u knjigu fonda; izvještaj stanja ih
   prikazuje kao „odlivi koji nisu u fondu“. Odluka: vezati ih za rashode ili knjigu fonda.
3. **`NumberYear` računa** je godina obračunskog perioda, ne godina izdavanja.
4. **Status `Draft`** računa postoji, ali se ne koristi (ukloniti ili uvesti nacrte računa).
5. **Storno uplata:** nema djelimičnog storna/povrata ni retroaktivnog datuma storna.

## Backlog

**Frontend**
- Lokalizacija iznosa na webu: `formatMoney` koristi `Intl` `bs-BA`; preglednik bez tog lokala prikazuje
  `145.00 KM` umjesto `145,00 KM` (rješenje: ručno formatiranje kao u mobilnoj aplikaciji).
- Mobilni prikaz 390 px: bočni meni s 6+ stavki se ne prelama → horizontalni scroll stranice.
- Ekran „Pristupi i veze naloga“ još sadrži i matricu prava (duplikat ekrana „Uloge i pristupi“).

**Backend**
- Default prava uloga za detaljne ključeve su u kodu (`GranularPermissions`), ne u bazi.
- `BankAccounts.Edit` / `BankAccounts.Deactivate` nemaju endpointe; nijedan modul nema akciju Delete.
- `/access/overrides/{id}/revoke` opoziva samo grube ključeve (detaljna prava kroz matricu).
- Stari (legacy) računi se i dalje izvode iz zaduženja i trenutne tarife.
- Append-only pravilo je provedeno samo u EF `SaveChanges`, bez triggera u bazi.
- Numeracija računa: nema tabele brojača ni SQL testa konkurentne numeracije preko više perioda.
- Saldo za CSV/TXT izvode — dodati po banci kada budu dostupni stvarni uzorci.
- Promjene prava stižu korisniku pri sljedećoj izmjeni ili fokusu prozora (nema server push-a).

**Mobilno i okruženje**
- Fizički UAT preko Expo Go čeka staging s javnim domenom i važećim TLS certifikatom (`MOBILE_UAT_CHECKLIST.md`).
- `npm audit` u `mobile/` prijavljuje ranjivosti u Expo/Metro build alatima (ne u kodu aplikacije).
- GDPR: nema automatske anonimizacije, retencijskih rokova ni subject exporta (`ACCESS_CONTROL_AND_PRIVACY.md`).

## Sljedeći korak

Ručna verifikacija u pregledniku prije nastavka kodiranja.
