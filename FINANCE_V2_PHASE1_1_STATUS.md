# Finansije V2 — faza 1.1, stabilizacija

Datum: 30.09.2026. Faza 2 (storno/korektivni dokumenti) nije pokrenuta.

## Implementacija

- `BillingDraftRequest.ManualLines` prihvata slobodne stavke po jedinici i fondu bez `TariffVersion`. Validiraju se opis, jedinica mjere, količina, cijena, tip/vrijednost popusta i obavezni razlog. Preview koristi isti plan i fingerprint kao knjiženje; ništa se ne zapisuje prije `Billing.Post`. Knjiženje u jednoj transakciji stvara `Charge` i `InvoiceLine` sa nullable `TariffVersionId`; `IsManualOverride` i razlog su trajni/auditirani. Postojeći tok `PaymentAllocation → Charge` nije promijenjen.
- `Fund.RequiresBankAccountForInvoice` je konfigurabilan. Za fond sa uključenim uslovom preflight navodi naziv fonda i blokira preview; posting provjerava opet. Postojeći fondovi ostaju na `false` radi kompatibilnosti, a ovlašten korisnik može promijeniti vrijednost kroz API/UI.
- `SimplePdfWriter` više ne transliterira BHS tekst. Ugrađeni Noto Sans TrueType sa OFL licencom koristi Type0/CIDFont, `CIDToGIDMap` i `ToUnicode`; nije potrebna instalacija fonta na serveru.
- `IInvoiceNumberProvider` izoluje format `INV-{year}-{sequence:000000}` i konfigurirani prefiks. Serijalizacija SQL transakcije i postojeći unique indeksi ostaju isti.
- UI ima formu `+ DODAJ RUČNU STAVKU`, automatski informativni izračun, preflight banner `OBRAČUN NIJE SPREMAN ZA KNJIŽENJE`, te eksplicitnu oznaku `LEGACY OBRAČUN (informativno)`. Post mutacija obavještava `zev:*:finance` pretplatnike i osvježava listu bez F5.

## Novi i promijenjeni fajlovi

- Backend: `src/Zev.Api/Finance/MonthlyBillingEngine.cs`, `src/Zev.Api/Finance/InvoiceNumberProvider.cs`, `src/Zev.Api/Domain/FinanceEntities.cs`, `src/Zev.Api/Data/ZevDbContext.cs`, `src/Zev.Api/Data/Migrations/ZevDbContextModelSnapshot.cs`, `src/Zev.Api/Data/Migrations/20260930190000_InvoiceV2Stabilization.cs`, `src/Zev.Api/Endpoints/FinanceEndpoints.cs`, `src/Zev.Api/Endpoints/InvoiceEndpoints.cs`, `src/Zev.Api/Endpoints/PaymentEndpoints.cs`, `src/Zev.Api/Program.cs`, `src/Zev.Api/Zev.Api.csproj`.
- PDF/font: `src/Zev.Api/Reporting/SimplePdfWriter.cs`, `src/Zev.Api/Reporting/Fonts/NotoSans-Regular.ttf`, `src/Zev.Api/Reporting/Fonts/OFL.txt`.
- Frontend: `web/src/admin/FinancePage.tsx`, `web/src/billingUi.ts`.
- Testovi: `tests/Zev.Api.Tests/BillingTests.cs`, `tests/Zev.Api.Tests/BillingMigrationTests.cs`, `tests/Zev.Api.Tests/PdfWriterTests.cs`, `tests/Zev.Api.Tests/TenantApiTests.cs`, `web/tests/billingUi.test.mjs`.
- Dokumentacija: `ARCHITECTURE.md`, `DOMAIN_MODEL.md`, `ERD.md`, `OPEN_BUSINESS_DECISIONS.md`, ovaj statusni dokument. Izvorna `20260930180000_InvoiceV2.cs` migracija nije mijenjana.

## Migracijska revizija

- `20260930180000_InvoiceV2` nije mijenjana. `IMigrator.GenerateScript` testira skriptu od prethodne migracije: samo `CREATE TABLE Invoices/InvoiceLines`, FK prema postojećim entitetima, check constraints za iznose/količinu i unique/lookup indeksi. Nema `DROP TABLE`, `DROP COLUMN` ni `ALTER TABLE Charges`.
- Nova `20260930190000_InvoiceV2Stabilization` dodaje `Funds.RequiresBankAccountForInvoice bit NOT NULL DEFAULT 0`, proširuje dvije `TariffVersionId` kolone sa `NOT NULL` na `NULL`, privremeno skida i vraća FK/indekse na `Charges`. Unique indeks tarifnih zaduženja isključuje `TariffVersionId IS NULL` kako bi više slobodnih stavki iste jedinice bilo moguće. `IMigrator.GenerateScript` testira generisani SQL bez spajanja na bazu: nema brisanja tabela, kolona ili finansijskih redova. Nullable proširenje nije gubitak podataka.
- Migracije **nisu primijenjene** na postojeću/produkcijsku bazu. Prije primjene napraviti backup, pregledati migracijski SQL na ciljnoj verziji SQL Servera i izvršiti u kontrolisanom deploymentu. `dotnet-ef` nije dostupan u ovom okruženju; SQL je generisan i provjeren kroz `IMigrator.GenerateScript` testove, ne kao samostalna `.sql` datoteka.

## Validacija

- Backend build: PASS, 0 warning/0 error.
- Backend testovi: PASS 116/116. Ciljani testovi pokrivaju slobodnu stavku, popust, nullable vezu, bank preflight, provider broja, migracijsku skriptu, BHS font i prijenos vlasništva nakon izdavanja (stari PDF identičan, novi vlasnik nema pristup).
- Frontend build/typecheck: PASS. Frontend testovi: PASS 30/30, uključujući post → refresh, ručni iznos/popust, V2 listu i legacy oznaku.
- SQL INTEGRATION TESTS: **NOT EXECUTED**. `ZEV_TEST_SQL_CONNECTION` nije postavljen, a Docker API u ovom sandboxu nije dostupan. Opt-in SQL testovi u xUnit broju formalno prolaze ranim izlaskom; to nije dokaz concurrency/performance rezultata.

Za izolovani testni SQL Server, koji smije kreirati/brisati isključivo vlastite nasumično imenovane testne baze:

```powershell
$env:ZEV_TEST_SQL_CONNECTION = 'Server=localhost,1433;Database=master;User Id=sa;Password=<TEST_ONLY_PASSWORD>;TrustServerCertificate=True'
dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --filter 'FullyQualifiedName~SqlBillingConcurrencyTests|FullyQualifiedName~SqlBillingPerformanceTests'
```

## Otvoreno

- Poslovna odluka: koji fondovi moraju imati aktivan bankovni račun, te da li novi fondovi po defaultu trebaju zahtijevati račun. Trenutna migracija ostavlja postojeću politiku netaknutom.
- PDF je tehnički Unicode snapshot, ne pravno odobren obrazac; formalni sadržaj i format zahtijevaju poslovnu/pravno-računovodstvenu potvrdu.
- Storno, korektivni dokumenti, automatsko korištenje preplate i SQL concurrency/performance validacija nisu dio potvrđenog rezultata faze 1.1.
