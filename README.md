# ZEV Platforma

Finansije V2, faza 2.1: dedupliciran timeline uplata i oslobađanja kredita, prikaz aktera, višestavčne korekcije i jasniji stornirani/korigovani računi u administratorskom i vlasničkom UI-ju. Validacija i otvorene provjere: `FINANCE_V2_PHASE2_1_STATUS.md`.

Finansije V2, faza 2: append-only storno i korektivni dokumenti nad trajnim `Invoice V2` računima, s reversal zaduženjima i oslobađanjem alokacija. Pregled implementacije, migracije i preostalih SQL/poslovnih provjera: `FINANCE_V2_PHASE2_STATUS.md`. Prije primjene nove migracije `20260930200000_InvoiceLifecycle` na postojećoj bazi napraviti backup i pregledati SQL skriptu.

Finansije V2 faze 1/1.1 i njihov historijski status: `FINANCE_V2_PHASE1_STATUS.md` i `FINANCE_V2_PHASE1_1_STATUS.md`. Aktuelna faza 2 je dokumentovana u `FINANCE_V2_PHASE2_STATUS.md`. Prije pokretanja na postojećoj bazi pregledati sve nove migracije i napraviti backup.

Postojeći ASP.NET Core 10 / EF Core / SQL Server sistem, React/Vite web i React Native/Expo mobilna osnova. Faze 2–9 su razvijane nad istom Fazom 1 i postojećim migracijama. Aktuelni status je **Functional Beta**, ne Production ready. Najnovija 32-kriterijska matrica i preostali gateovi su u `FINAL_TECHNICAL_CLOSURE.md`; `FINAL_ACCEPTANCE_STATUS.md` i `FINAL_IMPLEMENTATION_STATUS.md` su historijski presjeci.

## Lokalno pokretanje

Preduslovi: Docker Compose, Node.js 22+, npm; .NET 10 SDK za testove i ručne migracije. U PowerShell terminalu iz ovog direktorija:

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
# Urediti .env: SA_PASSWORD, JWT_SIGNING_KEY (najmanje 32 UTF-8 bajta), BOOTSTRAP_ADMIN_EMAIL, BOOTSTRAP_ADMIN_PASSWORD.
docker compose up --build -d
docker compose ps
Invoke-WebRequest http://localhost:8080/health/live
Invoke-WebRequest http://localhost:8080/health/ready
```

Web: `http://localhost:5173`. API: `http://localhost:8080`. Compose podiže `db`, `api`, `web`; podaci SQL Servera i prilozi imaju zasebne Docker volumene. U razvojnom profilu API primjenjuje EF migracije pri startu. Sačekati da baza postane spremna; `api` ima `restart: on-failure`. Nakon prvog kreiranja administratora ukloniti `BOOTSTRAP_ADMIN_PASSWORD` iz `.env` i ponovo pokrenuti API.

Testovi i build:

```powershell
dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj
npm ci --prefix web
npm run build --prefix web
npm ci --prefix mobile
npm run typecheck --prefix mobile
```

SQL performance test koristi privremenu izolovanu bazu, kreira je i briše; pokreće se kada je postavljen `ZEV_TEST_SQL_CONNECTION` na testni SQL Server (ne produkciju). Bez te varijable test se logički preskače ranim izlazom, ali xUnit ga broji kao prolaz; izvještaje treba jasno razlikovati.

Mobilno: u `mobile` direktoriju postaviti `EXPO_PUBLIC_API_URL` na HTTP(S) adresu API-ja dostupnu telefonu (za produkciju isključivo HTTPS), pa `npm start`. Mobilna osnova nije verificirana na uređaju i nije spremna za produkciju.

Za produkciju: kontrolisana migracija, TLS reverse proxy, secret store, backup/restore test i pravno/bankarska validacija obrazaca. Detalji u `FINAL_IMPLEMENTATION_STATUS.md`.

Detaljnije operativne upute: `LOCAL_DEVELOPMENT.md`, `UAT_GUIDE.md`, `DEPLOYMENT.md`, `BACKUP_RESTORE.md`, `SECURITY.md`, `MOBILE_UAT_CHECKLIST.md`, `DEPENDENCY_SECURITY_STATUS.md`.

Finansijski tok od stavke računa do bankovnog sravnjenja i kartice dokumentovan je u `BILLING_AND_PAYMENTS_WORKFLOW.md`. Migracija `20260928120000_BillingItemWorkflow` mora biti primijenjena prije korištenja novog API-ja; u Development Compose profilu API to radi pri startu. Jedna uplata ne prelazi različite fondove bez odobrene politike; mješoviti račun 53 KM nije potvrđen kao potpuno zatvoren nakon jedne uplate. Najnoviji test/build i neizvršeni Docker/browser koraci su u dodatku `FINAL_TECHNICAL_CLOSURE.md` od 28.09.2026.

UAT finansijski seed: iz korijena repozitorija pokrenuti `./SEED_UAT_FINANCE.ps1` u PowerShellu. Skripta izgradi/podigne samo API servis, sačeka `/health/ready`, pa eksplicitno uključi seed u Development režimu. Ne resetuje bazu niti briše podatke. Po želji prije pokretanja postaviti `UAT_FINANCE_MANAGER_PASSWORD` (najmanje 12 znakova) i opcionalno `UAT_FINANCE_MANAGER_EMAIL`; podrazumijevani email je `uat.finance.manager@example.test`. Lozinka se ne upisuje u skriptu ili izvorni kod. Detalji i očekivani iznosi su u `UAT_GUIDE.md`.
