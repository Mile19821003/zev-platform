# Status nakon cloud handoff ciklusa — 10.10.2026.

Ovaj dokument ažurira tabelu iz sekcije 9 dokumenta „ZEV Cloud Handoff – instrukcije za nastavak rada“.
Sva mjerenja su izvršena na stvarnom okruženju: lokalni SQL Server `ZEVTEST` (SQL Server 2025 Developer)
i izolovani Docker compose projekat (`mcr.microsoft.com/mssql/server:2022-latest`, API i web kontejner).

## Tabela statusa

| Oblast | Root cause | Izmjena | Test / mjerenje | Status |
|---|---|---|---|---|
| Billing 1.001 | — | — | `SqlBillingPerformanceTests` izolovano: 22,1 s za cijeli test (migracija + seed + preview + generisanje), prag < 30 s. `GenerateAsync` mapira u Conflict samo SQL greške 2601/2627. | **VERIFIED** |
| Registry 10K | Filter i ORDER BY bez zajedničkog indeksa | Indeks `IX_PropertyUnits_ZevId_EntranceId_Code_Id` (commit `1d6d385`) | Zvanični test: count+page 109 ms (ranije ~10,1 s). Actual plan: Index Seek, admin upiti 1–3 ms. | **VERIFIED** |
| Health | `health-smoke.ps1` padao u neinteraktivnom PowerShell 5.1 (`Invoke-WebRequest` bez `-UseBasicParsing`) | `-UseBasicParsing` u `scripts/health-smoke.ps1` i `LOCAL_DOCKER_E2E.ps1` | Docker: `/health/live` 200, `/health/ready` 200 (`database=up`); kad baza ne radi `/ready` = 503, `/live` = 200; oporavak bez restarta API-ja. | **VERIFIED** |
| Backup/Restore | — | — | `SqlBackupDrillTests` stvarno izvršen na Docker SQL-u: backup `COPY_ONLY`/`CHECKSUM`, `VERIFYONLY`, restore u zasebnu bazu (17 migracija, 52 tabele); dodatno `DBCC CHECKDB` = 0 grešaka. Baza je bila prazna (mehanizam, ne restore produkcijskih podataka). | **VERIFIED** |
| RACI/Hodogram | Funkcionalnost nije postojala | Novi modul (vidi ispod), migracija `20261010063920_HodogramRaci` | Unit + API + SQL concurrency testovi, Docker E2E 13/13, UI smoke u pregledniku (Edge) | **VERIFIED** |
| PWA (5.6) | — | Nije implementirano (poslovna odluka) | U `web/` ne postoji manifest ni service worker; PWA se ne spominje ni u jednom dokumentu zahtjeva. | **NOT APPLICABLE / SKIPPED BY DECISION** (Mobilni pristup pokriven kroz zasebnu Expo aplikaciju u `mobile/`) |
| Završna regresija (5.7) | — | — | Backend build 0 upozorenja; non-SQL 164/164; SQL paket 12/12 (lokalni `ZEVTEST`); web typecheck, 62/62 testova, build. | **VERIFIED** |

## PWA (5.6)

STATUS: NOT APPLICABLE / SKIPPED BY DECISION (Mobilni pristup pokriven kroz zasebnu Expo aplikaciju u `mobile/`).

Odluka vlasnika projekta od 10.10.2026: PWA se ne implementira jer je puni PWA uz namjensku Expo aplikaciju suvišan.

## Zaključak handoffa

Sve stavke iz sekcije 9 handoff dokumenta su zatvorene: Billing 1.001, Registry 10K, Health, Backup/Restore,
RACI/Hodogram i završna regresija su VERIFIED, a PWA je NOT APPLICABLE po odluci. Handoff i preuzimanje u
cloud okruženje su zaključeni.

## RACI / Hodogram (5.5)

- **Model:** `Hodogram` (organizacija) → `HodogramPhase` → `HodogramStep` → `HodogramAssignment` (uloga × R/A/C/I).
  Kolone matrice su vrijednosti `MembershipRole`. Kompozitni FK-ovi garantuju da korak i faza pripadaju istom hodogramu i organizaciji.
  Sve izmjene se automatski auditiraju (`ITenantRecord`).
- **Pravilo:** svaki korak ima najmanje jedan R i tačno jedan A; C i I su opcionalni. Nevažeća matrica → 422 s problemima po koraku.
- **Ponovno snimanje** primjenjuje razliku: nepromijenjene ćelije zadržavaju identitet, upisuju se samo stvarne promjene.
- **Konkurentnost:** `Hodogram.Revision` (concurrency token); zastarjela revizija → 409. Potvrđeno SQL testom s dva istovremena snimanja.
- **Pristup:** sistemski administrator i administrator organizacije; ostali dobijaju 404.
- **API:** `/api/organizations/{organizationId}/hodograms` — CRUD hodograma, faza i koraka; `PUT /{id}/matrix`.
- **UI:** meni „Hodogram“ u sistemskoj administraciji (izbor organizacije) i u radnom prostoru administratora organizacije.
- **Van opsega ove verzije:** globalni šabloni (bez organizacije) i vezivanje hodograma za pojedinačni ZEV.

## Tehnički dug — Faza 1 (riješeno 10.10.2026.)

- **Maskiranje `DbUpdateException`** u `ExpensePostingService` i `InvoiceLifecycleService`: uveden `Data/DbUpdateErrors.cs`
  koji kao poslovni konflikt priznaje samo konkurentni upis (`DbUpdateConcurrencyException`, SQL 2601/2627, deadlock 1205 —
  i kad ga `SqlServerExecutionStrategy` umota u `InvalidOperationException`). Ostale greške se propuštaju.
  Usput ispravljena postojeća greška: istovremeno knjiženje istog rashoda završavalo je s 500 (umotani deadlock nije bio hvatan).
- **Memorijski grant manager registry upita**: `Type` se čita kao `nvarchar(30)` i formatira nakon materijalizacije;
  grant 65.208 KB → 10.336 KB, bez `PlanAffectingConvert`/`MemoryGrantWarning`, JSON odgovor nepromijenjen.

## Tehnički dug (evidentirano, nije rješavano)

- **Široki `catch (DbUpdateException)` na još ~25 mjesta**: `ManagerRegistryWriteEndpoints` (10), `PaymentWorkflowService` (4),
  `PaymentReconciliationEngine` (3), `OwnershipService` (2), `FinanceEndpoints` (2), te po jedno u `ExpenseEndpoints`,
  `LegalEndpoints`, `MaintenanceEndpoints`, `PaymentEndpoints` i `VotingService`. Refaktorisati u sklopu redovnog održavanja
  pomoću `DbUpdateErrors.IsConcurrentWriteConflict`; provjeriti i umotani deadlock (vidi gore) na mjestima sa serializable transakcijom.
- **`UPDLOCK` za knjiženje rashoda**: `ExpensePostingService` čita rashod u serializable transakciji bez `UPDLOCK`, pa dva istovremena
  knjiženja završavaju deadlockom koji se sada ispravno prepoznaje kao konflikt (409). Preventivno rješenje je zaključati red rashoda
  pri čitanju (kao `InvoiceLifecycleService` sa `Zevs WITH (UPDLOCK, HOLDLOCK)`); ishod za korisnika ostaje 409, bez deadlocka na serveru.
- Jednokratni spike (10,8–25 s) na prvom izvršavanju manager registry upita uočen u lokalnom mjerenju; uzrok neutvrđen
  (potreban `VIEW SERVER STATE` za analizu čekanja).
- SQL Server predlaže indeks `(ZevId, BuildingId, EntranceId, IsActive)` za registry count — nije opravdan (upit 4,7 ms).
- SQL integration testovi bez `ZEV_TEST_SQL_CONNECTION` / `ZEV_BACKUP_DRILL_CONNECTION` završavaju odmah kao PASS; filter `Sql` uključuje i 4 testa bez baze. Rezultat „SQL x/x“ zato treba čitati uz napomenu koje varijable su bile postavljene.
- `/health/ready` vraća 503 tek nakon ~17 s kad baza ne radi (podrazumijevani connection timeout 15 s).
