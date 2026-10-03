# Završni tehnički prolaz — 27.09.2026.

## Novi presjek — upravljanje lozinkama, 30.09.2026.

Dodani su reset od strane System Administratora, centralna `PasswordPolicy`, kriptografski generator u web formi, obavezna promjena nakon prijave, samostalna promjena uz staru lozinku, ograničenje JWT-a dok je promjena obavezna, revokacija refresh sesija i `SecurityVersion` provjera svakog access/refresh tokena. Mobilna osnova ima isti prisilni i self-service tok te novi refresh token sprema u SecureStore. Password/hash se ne vraćaju kroz API niti se upisuju u audit. `PASSWORD_MANAGEMENT_UAT.md` opisuje ručnu provjeru.

Migracija `20260930150000_PasswordManagement` je dodatna: kolone `MustChangePassword`/`SecurityVersion` u `Users`, `SecurityVersion` u `RefreshSessions` i šire audit polje `Action`; postojeći zapisi ostaju s verzijom 0. Snapshot i SQL-generacijski test prolaze. **Migracija nije primijenjena na postojeću SQL bazu u ovom prolazu**; prije rollouta uraditi backup i UAT. Build API-ja prolazi bez warning/error; backend testovi **102/102 PASS**, web testovi **25/25 PASS**, web build **PASS**, mobile typecheck **PASS**. SQL opt-in testovi bez odgovarajuće konekcije nisu dokaz stvarne SQL migracije. Fizički uređaj, browser UAT, SQL rollout i Docker image build nisu izvedeni. Status ostaje **Functional Beta, ne Production Ready**.

## Novi presjek — multi-ZEV access i privacy, 30.09.2026.

Ovaj presjek nadograđuje, ne retroaktivno prepisuje, historijsku matricu ispod. Dodani su `EffectiveAccessService`, `OrganizationAdministrator` scope, automatski Owner iz `Verified Person` + aktivnog `Ownership`, `Pending/Verified/Rejected/Revoked` workflow veze, `AccessOverride` Grant/Deny s razlogom/rokom/opozivom, osnovni admin UI, aktivni ZEV kontekst, person/unit/role vidljivost dokumenta i ponovno provjeren download. Naslijeđeni `Owner` membership ne otvara više ni objave/dokumente ciljane na Owner ulogu; owner DTO regresija provjerava da ne izlaze tuđi email/telefon/JIB. Finansijski PDF/XLSX export ima audit trag. Bivši owner nema aktivni ZEV pristup; opciono (`Privacy:HistoricalSelfAccess`, default false) vidi samo svoju istorijsku finansijsku karticu/račun za period vlasništva. `ACCESS_CONTROL_AND_PRIVACY.md` i `OWNER_ACCESS_UAT.md` opisuju tačan obim i ručni test.

Nova migracija `20260930120000_EffectiveAccessPrivacy` je forward-only: postojeće organization-wide Manager članstvo postaje OrgAdmin, stare veze su `Pending`, a business tabele se ne brišu. **Migracija nije primijenjena na postojeću lokalnu SQL bazu u ovom prolazu** i nijedan SQL volume nije diran. `BillingMigrationTests.CurrentSqlServerModelMatchesMigrationSnapshot` potvrđuje da nema pending model diff-a, a test generiše inkrementalni SQL. `dotnet ef migrations has-pending-model-changes` CLI nije mogao biti pokrenut jer `dotnet-ef` nije instaliran; rezultat ne treba predstavljati kao izvršen CLI check.

Završne provjere ovog presjeka: `dotnet build` **PASS, 0 warnings/errors**; backend `dotnet test` **96/96 PASS**; `npm ci --prefix web` **PASS** uz lokalni workspace npm cache; web `npm test` **24/24 PASS**; `npm run build --prefix web` **PASS**; `docker compose config --quiet` **exit 0** uz upozorenje da korisnički Docker config nije čitljiv. SQL opt-in varijable nisu postavljene, pa testovi koji rano izlaze nisu novi SQL proof. Nisu izvedeni browser/persona UAT, primjena migracije, Docker image build/health niti GDPR/pravna potvrda. Retencija, legal hold, subject export, anonimizacija i politika objavljivanja zajedničkih finansijskih agregata ostaju otvoreni u `OPEN_BUSINESS_DECISIONS.md`. Status ostaje **Functional Beta, ne Production Ready**.

## 1. Executive summary

Rad je nastavljen nad postojećim ZEV projektom i bazom bez resetovanja. Dodani su manager-scoped registry CRUD i ownership UI, SQL 10k pagination test, upload ekstenzija/path validacija, četiri SQL konkurentne regresije, health/Docker E2E skripte i operativna dokumentacija. **Status ostaje Functional Beta, ne Production Ready.** Stvarni novi Docker E2E, bank return/reversal, kompletan izvještajni i dokument workflow, mobilni uređajski UAT i NuGet sken nisu zatvoreni.

## 2–3. Acceptance prije i poslije

| Presjek | PASS | PARTIAL | FAIL | BLOCKED |
|---|---:|---:|---:|---:|
| Prije (`FINAL_ACCEPTANCE_STATUS.md`, historijski) | 13 | 17 | 0 | 2 |
| Poslije ovog tehničkog prolaza | **20** | **10** | **0** | **2** |

Sedam od 17 ciljnih PARTIAL kriterija prelazi u **tehnički PASS** (1–4, 30–32). PASS za registar znači automatizovani API workflow, tenant/RBAC regresiju, server-side paginaciju i build; ne predstavlja ručni browser/persona UAT. Dva BLOCKED kriterija (16 i 23) nisu preimenovana. Prethodnih 13 PASS ostaje nepromijenjeno; cijela matrica 32 kriterija time ima 20 PASS, 10 PARTIAL, 0 FAIL, 2 BLOCKED.

## 4. Ciljana matrica (17)

| ID | Before | After | Evidence | Remaining work |
|---|---|---|---|---|
| 1 Admin organizacija | PARTIAL | **PASS** | `TenantApiTests.SystemAdministratorCanCreateRegistryChainAndUpdateOrganizations`: create/edit/status; Admin UI; latest API admin list 200 | Browser persona UAT na ne-praznoj staging bazi |
| 2 Admin ZEV | PARTIAL | **PASS** | Isti API workflow test kreira/uređuje/status ZEV, build web/API; admin list 200 | Browser persona UAT |
| 3 Zgrada/ulaz | PARTIAL | **PASS** | Novi manager create/edit/status API, cross-ZEV/inactive-membership test, manager web CRUD build | Browser UAT, veći lookup UX |
| 4 Nekretnine | PARTIAL | **PASS** | Manager CRUD/filter/page; SQL 10.000 jedinica, 5.000 filtriranih, 20 vraćenih, `OFFSET/FETCH`; tenant test | Ponoviti 10k preko HTTP i UI na stagingu |
| 5 Vlasnici | PARTIAL | PARTIAL | Manager Person CRUD i ownership add/close UI/API, historijski test | Manager-created lice bez ownershipa nije trajno vidljivo u ZEV listi; user/tenant linking i owner-user odnos nisu kompletan workflow |
| 11 Automatsko sravnjenje | PARTIAL | PARTIAL | 200/200 sintetičkih validnih jednoznačnih referenci, nejasan isti iznos/različiti vlasnici ne bira nasumično, partial/overpayment i duplicate test | Bank return/reversal s auditom i alokacijama; stvarni anonimizirani bankarski uzorak |
| 13 Uplatnice | PARTIAL | PARTIAL | Owner PDF i tuđa uplatnica 404 u `TenantApiTests`; QR provider nije konfigurisan | PDF transliterira dijakritiku, nema `PaymentReferenceProvider`, službenog formata/QR standarda |
| 15 Dugovanja | PARTIAL | PARTIAL | As-of/open-items/aging API, historical owner regression, XLSX | UI svi filteri i aging dashboard, export paritet, posebno SQL as-of integracijski test |
| 17 Pravni predmeti | PARTIAL | PARTIAL | CRUD/status/file link API i osnovni UI | Traženi statusi Draft/Active/Suspended/Cancelled nisu u modelu, detail/attachment/audit/persona test |
| 20 Periodično održavanje | PARTIAL | PARTIAL | Plan/schedule/calendar API i recurrence test | Edit plan, pun month/upcoming/overdue/completed UI, attachment i persona UAT |
| 22 Dokumenti | PARTIAL | PARTIAL | Upload/list/download, owner visibility/raw IDOR, extension/size/magic-byte test | UI search/filter/preview/archive i kompletna dozvola svake akcije |
| 24 Izvještaji | PARTIAL | PARTIAL | Summary, annual PDF, open-items XLSX, owner card i tenant export test | 13 traženih izvještaja, filteri i identični XLSX/PDF exporti nisu potpuni |
| 25 Vlasnički portal | PARTIAL | PARTIAL | Finansije/nekretnine/uplatnica + community/maintenance UI, izolacija owner-a u API testu | Puni E2E, historijski filter u UI, galerija fotografija, mobile runtime, display pravilo glasanja |
| 29 Docker Compose | PARTIAL | PARTIAL | `docker compose config --quiet` PASS; `LOCAL_DOCKER_E2E.ps1` spreman | Sandbox nema pristup `npipe/docker_engine`; novi build/up/ps/login/cross-tenant E2E nije izvršen |
| 30 `/health/live` | PARTIAL | **PASS** | Najnoviji lokalni API build direktno na `127.0.0.1:8081`: `scripts/health-smoke.ps1` HTTP 200 | Ponoviti na novom Compose image-u |
| 31 `/health/ready` | PARTIAL | **PASS** | Isti direktni API build/realni lokalni SQL: HTTP 200, `database=up` | Ponoviti na novom Compose image-u |
| 32 Dokumentacija | PARTIAL | **PASS** | Ažurirani README/arhitektura/model/ERD/plan/odluke/faza linkovi i novi lokalni/UAT/deploy/backup/security/closure dokumenti | Operativno ažurirati nakon rješavanja otvorenih gateova |

## 5–6. Testovi i web build

- Backend: `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore -p:NuGetAudit=false` **PASS, 0 warnings, 0 errors**. `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore -p:NuGetAudit=false` sa obje SQL varijable: **63 total / 63 passed / 0 failed / 0 skipped u xUnit prikazu**.
- SQL testovi su **stvarno izvršeni** jer su postavljeni `ZEV_TEST_SQL_CONNECTION` i `ZEV_BACKUP_DRILL_CONNECTION`: izolovani 1001-charge test, izolovani 10k registry test, backup/verify/restore drill i tri race testa (billing, reconciliation, ownership+vote). Bez varijabli ovi testovi izlaze rano i nisu stvarno izvršeni iako xUnit kaže passed.
- Web: `npm ci --prefix web` i `npm run build --prefix web` **PASS** (TypeScript + Vite). Ručni browser/persona UAT nije izvršen.

## 7. Mobile

`npm ci --prefix mobile` i `npm run typecheck --prefix mobile` **PASS**. `npm audit --prefix mobile --json`: **23 moderate, 0 high/critical**. Paket-po-paket trijaža u `DEPENDENCY_SECURITY_STATUS.md`; bez prisilnog major upgrade-a. Android/iOS emulator ili fizički uređaj: **NOT EXECUTED**; `MOBILE_UAT_CHECKLIST.md` je otvoren.

## 8–9. Docker E2E i health

`docker compose config --quiet` **PASS**. `docker info` vraća permission denied za Windows named pipe; novi `docker compose build`, `up -d`, `ps` i puni Docker E2E **NOT EXECUTED**. Nije korišten `down -v` niti reset baze. `LOCAL_DOCKER_E2E.ps1` lokalno gradi i podiže stack, čeka health, a uz procesne testne credentials provjerava login/`/api/me` i relevantne liste. Svaki SKIPPED korak treba posebno zatvoriti.

Najnoviji backend DLL pokrenut je direktno u Testing profilu na `127.0.0.1:8081` protiv postojeće lokalne SQL baze: live **200**, ready **200** i `database=up`. Admin login, `/api/me`, organizacije i ZEV liste **200**. Baza u tom smoke-u nije sadržavala ZEV/organizaciju, pa realni registry/report GET nije izvršen nad njom; integracijski API test pokriva tok na izolovanim podacima. Ovo nije dokaz najnovijeg Compose image-a.

## 10–11. Performance i security scan

SQL test je ubacio 10.000 PropertyUnit, filtrirao 5.000 aktivnih i vratio 20 redova; provjeren SQL `OFFSET/FETCH` i pretraga oznake. Jedan lokalni count+page run trajao je oko **75 ms**, bez SLA tvrdnje i bez HTTP/UI load testa. SQL billing 1001 charge prošao prag <30 s; najnoviji cijeli test trajao je približno 7 s. Nisu profilisani svi najčešći query scenariji.

Ponovljeni web npm audit: **0** prijavljenih ranjivosti na zaključanom lockfile-u. Mobile: **23 moderate**. Ponovljeni `dotnet list ... package --vulnerable --include-transitive` je **NOT VERIFIED** jer NuGet feed odbija `NU1900`; build sa `NuGetAudit=false` nije security scan. Nije izvršen penetration test.

## 12–13. Concurrency i finansijske invarijante

Sve četiri tražene race situacije su **stvarno izvršene na izolovanim SQL bazama**: dva mjesečna obračuna istog perioda (`SqlBillingConcurrencyTests`), dvije ručne alokacije iste bank transaction (`SqlReconciliationConcurrencyTests`), dva konfliktna ownership upisa i dva identična glasa (`SqlOwnershipVoteConcurrencyTests`). Svaka ima jednog pobjednika bez duplikata; billing oba zahtjeva vraćaju isti run. Prvi billing test je otkrio stvarni SQL deadlock; dodati su transakcijski `UPDLOCK/HOLDLOCK` po ZEV-u, bank transaction, unit i ballot eligibility, nakon čega svi prolaze. Ovo je lokalni race regression, ne opšti load/soak test.

Novi test `ManualAllocationCannotExceedPaymentOrMixOwners` provjerava limit iznosa i vlasnika; `AmbiguousReferenceWithEqualAmountNeverChoosesAnOwner` provjerava ručni red. Raniji `PartialPaymentAndOverpaymentPreserveUnallocatedRemainder`, owner as-of history, expense posting/reversal i append-only testovi prolaze. Bank return/reversal i zasebna provjera svih finance invariants pod SQL konkurencijom ostaju otvoreni.

## 14–15. Baza i dokumentacija

Postoji **8 ranijih EF migracija**, od `20260927055031_InitialIdentity` do `20260927153424_Phase7CommunityVoting`; ovaj prolaz nije mijenjao shemu niti bazu resetovao. `dotnet-ef migrations has-pending-model-changes --no-build` kaže **No changes**. Izolovani SQL test primijenio je sve migracije; postojeća baza ostala je sačuvana. Historijski statusi `FINAL_ACCEPTANCE_STATUS.md` i `FINAL_IMPLEMENTATION_STATUS.md` nisu prepisani, već označeni kao raniji presjeci putem README/novih referenci. Novi dokumenti: `LOCAL_DEVELOPMENT.md`, `UAT_GUIDE.md`, `DEPLOYMENT.md`, `BACKUP_RESTORE.md`, `SECURITY.md` i ovaj izvještaj.

## 16–18. Otvorene stavke i produkcijski status

Preostalih **10 tehničkih PARTIAL**: **5, 11, 13, 15, 17, 20, 22, 24, 25, 29**. Poslovno/pravno **BLOCKED**: **16 opomene** (tekst/rok/dostava/kamata) i **23 formalno glasanje** (quorum/eligible/majority/weighting). Ne izmišljati vrijednosti; vidjeti `OPEN_BUSINESS_DECISIONS.md`.

**Produkcijski blockeri:** siguran bank return/reversal i stvarni bank sample; puni dokument/report/owner/maintenance/legal workflow; širi SQL load/soak i query profiling; najnoviji Compose E2E + cross-tenant negativni test; mobilni uređajski UAT i 23 npm moderate nalaza; NuGet sken; off-host backup/restore/RPO-RTO; službena uplatnica/Unicode/QR i pravna odobrenja. Nema osnova za oznaku Production Ready.

## Dodatak — finansijski workflow 28.09.2026.

Ovaj dodatak ne mijenja historijsku 32-kriterijsku matricu iznad. Dodani su konfigurabilni atributi `TariffVersion`, draft preview/fingerprint i transakcijsko knjiženje, račun/PDF/ZIP izveden iz stvarnih `Charge` redova, stabilna account referenca, automatsko raspoređivanje po dospijeću unutar jednog fonda, credits lista, kartica jedinice i puni bankovni povrat uz očuvanje originala. Novi EF migration ID je `20260928120000_BillingItemWorkflow`. Tehnički puni storno **nije** odobrenje za djelimične povrate ili konačnu računovodstvenu politiku.

Stvarno izvršeno u ovom prolazu: `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore -p:NuGetAudit=false` **PASS, 0 warnings/errors**; `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore -p:NuGetAudit=false` **74/74 PASS**; `npm ci --prefix web --cache .npm-cache` **PASS, 0 audit vulnerabilities** i nakon toga `npm run build --prefix web` **PASS**; `docker compose config --quiet` **PASS uz upozorenje o nedostupnom korisničkom Docker config-u**. Obični `npm ci --prefix web` je prvo pao zbog `EPERM` nad korisničkim npm cache-om; workspace cache je riješio problem. `BillingMigrationTests` potvrđuje `HasPendingModelChanges()==false` i generiše SQL script najnovije migracije bez spajanja na bazu. SQL opt-in varijable nisu bile postavljene u ovom prolazu; SQL testovi koji rano izlaze nisu dokaz novih SQL performance/race scenarija.

**Nije izvršeno:** `dotnet ef migrations has-pending-model-changes` CLI (`dotnet-ef` nedostupan i NuGet feed nije dostupan), primjena nove migracije na postojeći SQL Server, Docker build/up/E2E (sandbox nema pristup `npipe/docker_engine`), ručni browser UAT bez F5, stvarni bankarski uzorci. `FINAL_TECHNICAL_CLOSURE.md` se zato ne smije čitati kao novi Production Ready PASS. Po dostupnosti Dockera prvo backup, zatim `docker compose up --build -d` bez `down -v`, provjera migracije i health-a te UAT iz `BILLING_AND_PAYMENTS_WORKFLOW.md`.

**Otvoreno:** mješoviti račun redovnog i rezervnog fonda ne može se jednim transferom automatski zatvoriti jer bankovni račun i uplata pripadaju jednom fondu; stanje 53 KM (30+18+5) poslije 53 KM uplate na redovni račun ostavlja 18 KM rezervnog fonda otvoreno, 18 KM kao nepovezani kredit u redovnom fondu i `PartiallyMatched` red. Potrebna je odobrena međufondovska politika, zatim SQL i browser regresija. Djelimični bankovni povrat, SQL/UAT validacija historijskog as-of oko povrata, službeni PDF/Unicode/QR, potvrđeni domaći format banke i potpuni korisnički UI za sve izvještaje takođe ostaju otvoreni.
