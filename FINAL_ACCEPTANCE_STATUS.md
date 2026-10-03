# Finalni acceptance — 27.09.2026.

## Executive summary i status

**Functional Beta** (jedini odabrani status; nije Pre-production niti Production ready). Nastavljen je postojeći projekat/baza, bez resetovanja. Tehnički backend, web i mobilni typecheck prolaze, a SQL obračun i backup/restore su izvršeni na lokalnom SQL Serveru. Međutim manager registry UI je read-only, dio izvještaja i workflow-a nije kompletan, mobilni runtime i novi Docker E2E nisu izvršeni, NuGet scan nije verificiran i pravno/bankarska pravila nisu odobrena. **13 PASS, 17 PARTIAL, 0 FAIL, 2 BLOCKED** za 32 funkcionalna kriterija. PASS označava tehnički dokaz za navedeni opseg, ne pravnu/produkcijsku certifikaciju.

## Acceptance matrica

| ID | Criterion | Status | Evidence | Remaining action |
|---|---|---|---|---|
| 1 | Admin kreira organizaciju | PARTIAL | API i Admin UI postoje; build prolazi | Persona create/update/status UAT. |
| 2 | Admin kreira ZEV | PARTIAL | API i Admin UI postoje | Stvarni workflow UAT. |
| 3 | Zgrada/ulaz | PARTIAL | Model/API/UI i migracije | Manager CRUD, persona UAT. |
| 4 | Nekretnine | PARTIAL | Model/API/Admin UI | Manager CRUD, lista na 10k podataka. |
| 5 | Vlasnici | PARTIAL | Person/Ownership API | Manager UI za unos/izmjenu. |
| 6 | Historija vlasništva | PASS | `OwnershipTimelineTests`, `HistoricalBalanceKeepsPriorOwnerAndIgnoresLaterAllocation` | Potvrditi pravilo promjene unutar mjeseca. |
| 7 | Tarife | PASS | `BillingTests`, verzije/snapshot | Poslovno odobrenje tarifa. |
| 8 | Mjesečna zaduženja | PASS | SQL test 1001 zaduženje <30 s | Konkurentni load benchmark. |
| 9 | Fondovi | PASS | Ledger/Fund testovi | Produkcijska rekonsilijacija. |
| 10 | Bankarski import | PASS | CSV/TXT/XML/CAMT parser, deduplikacija i read-only preview testirani | Stvarni bankarski uzorci. |
| 11 | Automatsko sravnjenje | PARTIAL | Sintetički 200/200 validnih jednoznačnih referenci (100%); 11 nejasnih u ručnom redu | Potvrda na stvarnom bankarskom uzorku i povrati. |
| 12 | Ručno sravnjenje | PASS | Allocation/reconciliation testovi | Persona UAT. |
| 13 | Uplatnice | PARTIAL | Informativni PDF testiran | Službeni Unicode/QR provider i potvrda banke. |
| 14 | Rashodi | PASS | `ExpenseTests`, knjiženje/storno | Opcionalni approval workflow. |
| 15 | Dugovanja | PARTIAL | Debtors/open-items API, as-of i aging bucket korekcija; regresija | UI/filter/export i SQL integracija. |
| 16 | Opomene | BLOCKED | Informativni PDF/API postoji | Pravno odobren tekst, rokovi, dostava i kamate. |
| 17 | Pravni predmeti | PARTIAL | Legal API/UI postoji | Persona UAT, approval, audit test. |
| 18 | Prijava kvara | PASS | Maintenance testovi i API/web tok | Uređajski foto UAT. |
| 19 | Radni nalozi | PASS | Maintenance workflow/testovi | Kompletirati UI priloga/servisera. |
| 20 | Periodično održavanje | PARTIAL | Plan/schedule API | Mjesečni kalendarski UI i UAT. |
| 21 | Oglasna tabla | PASS | Scope API/UI, build | Prilozi i persona UAT. |
| 22 | Dokumenti | PARTIAL | Visibility test i raw file IDOR regresija | Cjelovit UI upload/download/preview/deactivate. |
| 23 | Glasanje | BLOCKED | Tehnički voting testovi, zaključavanje | Formalno pravilo kvoruma, glasača, pondera i pravna potvrda. |
| 24 | Izvještaji | PARTIAL | Summary, annual PDF, open-items XLSX, as-of filter | Preostali izvještaji/filteri/XLSX/PDF paritet. |
| 25 | Vlasnički portal | PARTIAL | Vlastita finansijska izolacija testirana | End-to-end sve owner funkcije. |
| 26 | RBAC/tenant isolation | PASS | API tenant, role, dokument i manager export/preview IDOR testovi | Šira matrica svakog endpointa i concurrency. |
| 27 | API i web build | PASS | Lokalni `dotnet build` 0 warning/error; `npm run build` PASS | CI ponavljanje. |
| 28 | Automatizovani testovi | PASS | Finalni backend run sa SQL vars: 47/47, 0 fail; backup drill pojedinačno 1/1 | Proširiti coverage i UAT. |
| 29 | Docker Compose kompletan sistem | PARTIAL | `docker compose config --quiet` PASS; korisnik ranije ručno potvrdio db/API/web | Novi `up --build -d` i E2E blokira Docker pipe dozvola sandboxa. |
| 30 | `/health/live` HTTP 200 | PARTIAL | Korisnik je ranije potvrdio 200; raniji lokalni API smoke | Ponoviti na novom Compose buildu. |
| 31 | `/health/ready` HTTP 200 | PARTIAL | Korisnik je ranije potvrdio 200/database=up; SQL test spaja se na bazu | Ponoviti na novom Compose buildu. |
| 32 | Dokumentacija | PARTIAL | Arhitektura/faze + novi drill/security/UAT/acceptance dokumenti | Uskladiti po završetku svih produkcijskih gateova. |

## Build, test, migracije i E2E

- Backend: `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore -p:NuGetAudit=false` PASS, 0 warning/error. `dotnet test ...` sa `ZEV_TEST_SQL_CONNECTION` i `ZEV_BACKUP_DRILL_CONNECTION`: **47 total, 47 passed, 0 failed, 0 skipped u xUnit prikazu**. Oba opt-in testa su stvarno aktivirana; bez varijabli rani povrat bi bio lažno prikazan kao PASS. Sintetičko sravnjenje: 200/200 (100%) jednoznačnih validnih referenci, ali to nije validacija stvarne banke.
- EF: osam postojećih migracija, zadnja `20260927153424_Phase7CommunityVoting`; `migrations has-pending-model-changes` kaže nema promjena. Nije dodata migracija niti resetovana baza. Restore kopija potvrdila najmanje 8 migration-history redova i 20 tabela.
- Web: `npm ci` i `npm run build` PASS. Mobile: `npm ci` i `npm run typecheck` PASS; emulator/fizički uređaj **NOT EXECUTED**. Nema mobile test skripte.
- Docker: `docker compose config --quiet` PASS. `docker info` odbijen (`npipe docker_engine permission denied`), pa `up --build -d`, `ps` i novi login/cross-tenant E2E **NOT EXECUTED**. `http://localhost:8080/health/live` i `/health/ready` na postojećem lokalnom servisu sada daju HTTP 200 (`database=up`), ali taj servis nije ponovno izgrađen iz ove izmjene. Korisnikova ranija ručna provjera nije ekvivalent novom E2E runu.
- SQL performance: opt-in test izolovane baze/migracija/1001 zaduženja **PASS <30 s**; 10k lista, owner ledger/debtor/bank/doc query profiling i paralelni billing nisu izvedeni.
- Backup/restore: **PASS lokalni drill**; `Zev_TestBackup.bak`, `VERIFYONLY`, restore u `ZevRestoreTest`, migration/table/read provjere i uklanjanje testne baze. Detalji u `BACKUP_RESTORE_DRILL.md`; off-host restore i `DBCC CHECKDB` nisu urađeni.
- Security scan: web npm 0; mobile npm **23 moderate**; NuGet **NOT VERIFIED/NU1900**. Detalji u `DEPENDENCY_SECURITY_STATUS.md`. Upload validacija, JWT/refresh/RBAC, rate limit upload/importa i raw document download pregledani, ali nije izvršen puni penetration test.

## Persona UAT

| Persona | Rezultat | Razlog |
|---|---|---|
| System Administrator | PARTIAL | Ranije potvrđen login; CRUD i globalni tok nisu ponovno ručno odigrani. |
| Manager | PARTIAL | Scope backend postoji; registry UI nema CRUD. |
| Representative | PARTIAL | Maintenance dozvole postoje; nema potpunog role UAT-a. |
| Owner | PARTIAL | Izolacija finansija/dokumenata automatizovana; mobilni i puni portal tok nisu ručno odigrani. |

## Odluke, blokade i backlog

`BUSINESS_DECISIONS_FOR_UAT.md` sadrži 28 otvorenih pitanja, **12 preliminarno kategorije A**. Nije donesena nijedna poslovna/pravna odluka u ime korisnika.

**Produkcijski blockeri:** potvrđena finansijska/pravna pravila i službeni dokumenti; manager registry i nepotpuni UI/izvještaji; siguran bank return/reversal i realni bankarski sample; rashodni approval po odluci; puna role/UAT i mobilni uređajski test; novi Docker E2E/health; mobile dependency trijaža + NuGet scan; off-host backup/restore/RPO-RTO; konkurentni i 10k query/load testovi. Non-blocking backlog: prilagodba informativnog PDF fonta, UX/paginacija, dodatni indeks nakon mjerenja, prošireni eksporti — samo gdje ne blokiraju dogovoreni prvi rollout.

## Deployment, rollback, backup i prvih 24h

1. **Deployment:** zatvoriti kategoriju A i scope odluke; staging kopija s anonimiziranim podacima; `npm ci`/build, `dotnet build/test`, NuGet/npm scan, sve migracije i DB diff; testirati sve persone i bankarske uzorke; postaviti TLS, secret store, tačan CORS/proxy, minimalni SQL pristup; backup+verify+restore; kontrolisano primijeniti migracije; podići API/web; provjeriti live/ready/login/cross-tenant i finansijski smoke; ukloniti bootstrap tajnu.
2. **Rollback:** zaustaviti pisanje, zabilježiti deployment/migration verziju, sačuvati incident log; vratiti kompatibilan API/web samo ako shema dozvoljava; inače uz odobrenje i plan oporaviti **novu** bazu iz verifikovanog backup-a pa preusmjeriti saobraćaj. Ne pokretati `docker compose down -v` i ne restore preko postojeće baze ad hoc.
3. **Backup:** odrediti RPO/RTO/retenciju, odvojeni šifrirani/off-host backup storage, `BACKUP ... WITH CHECKSUM`, `RESTORE VERIFYONLY`, periodični restore drill, provjera migracija i `DBCC CHECKDB`, evidencija uspjeha/alarma.
4. **Prvih 24h:** pratiti live/ready, login/refresh greške, 401/403/404 i rate-limit, SQL greške/deadlock, vrijeme obračuna/izvještaja, bank import/reconciliation queue, audit zapise, kapacitet DB/file storage-a, backup job/verify i alarmni kanal; imati dežurnog vlasnika incidenta i rollback kriterij.
