# Finansije V2 — Faza 3: Payments V2

Datum: 01.10.2026. Postojeći `Invoice → InvoiceLine → Charge ← PaymentAllocation ← Payment` ostaje finansijski model. Ovaj dokument opisuje stanje koda, ne potvrđuje primjenu migracije na lokalni SQL Server.

## Implementacija

- `Payment.SourceType` razlikuje `BankTransaction` i `Manual`; ručni unos ne stvara lažni bankovni red. Ručni unos zahtijeva razlog, metodu, osobu, fond, valutu, datum i idempotency `RequestId`; opciono odmah raspoređuje uplatu ili ostavlja kredit. Gotovina je isključena dok se eksplicitno ne uključi `Payments:AllowManualCash`.
- Kredit je izveden iz `Payment.Amount − SUM(PaymentAllocation.Amount)` za knjižene uplate. Ručna i automatska raspodjela koriste isti izvorni Payment; ne stvaraju novi novac. Negativna alokacija oslobađa kredit nakon storna računa ili ispravke pogrešne raspodjele.
- `PaymentWorkflowService` validira ZEV, organizaciju, osobu, fond, valutu, otvoreni Charge i preostali iznos. Za SQL Server koristi serijaliziranu transakciju i zaključavanje ZEV reda. `OperationId` i indeks omogućavaju siguran retry alokacije, a `ReversesAllocationId` sprečava dvostruku ispravku.
- Automatski prijedlog prati dospijeće, period i ID, samo unutar osobe/fonda/valute. Politika `Zev.AutoApplyPaymentCredit` je podrazumijevano isključena; pri knjiženju obračuna može primijeniti kredit nakon stvaranja računa. Preview odvaja novo zaduženje, raspoloživi/predloženi kredit i iznos za uplatu; originalni Invoice/PDF snapshot se ne mijenja.
- Ručno sravnjenje, identifikacija uplatioca, ispravka alokacije i automatska raspodjela su server-side zaštićeni postojećim granularnim pravima. EF audit bilježi aktera, vrijeme i promjene; razlog ručne uplate/ispravke je u trajnom redu. Bankovni izvod preračunava agregatni status poslije ručne raspodjele ili ispravke.
- Operativni ekran **Uplate i izvodi** ima statusne kartice, filtre, paginaciju, detalje match pravila, manualni unos, pregled kredita, raspodjelu na više Charge redova i append-only ispravku. Odvojeni ekran **Bankovne transakcije** ima server-side filtre. Invoice timeline prikazuje jednu uplatu s proširivim detaljima alokacija; kartica vlasnika/jedinice prikazuje događaje i izvedene salde.
- Bankovni checksum/import, tačna i stabilna referenca, postojeći full-return i legacy Charge ostaju podržani. Ne radi se automatski match samo po sličnosti imena.

## Migracija i pokretanje

`20261001100000_PaymentsV2` dodaje `Zevs.AutoApplyPaymentCredit`; čini `Payments.BankTransactionId` nullable uz kontrolu izvora, dodaje manualna/audit polja i indekse; dodaje `PaymentAllocations.OperationId`, `ReversalReason`, novi CHECK i indeks. Postojeći finansijski redovi/tabele se ne brišu. `Down` je namjerno blokiran da se ne izgubi istorija. Prije puštanja napraviti backup, pregledati migracijski SQL i primijeniti `dotnet ef database update --project src/Zev.Api --startup-project src/Zev.Api` na ciljanu bazu. Development Compose API može pozvati `MigrateAsync` pri startu. Ne koristiti `docker compose down -v`.

## Provjera i ograničenja

- `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore -v:q`: **PASS**, 0 warnings, 0 errors. `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore -v:q`: **PASS 142/142**. Broj uključuje opt-in SQL test koji se bez connection stringa završi bez stvarnog SQL rada.
- `npm run build --prefix web`: **PASS**. `npm run test --prefix web`: **PASS 49/49**.
- Generisanje inkrementalnog SQL-a i podudaranje EF modela sa snapshotom prolaze bez konekcije. To nije isto što i stvarna primjena migracije.
- SQL Server migracija i SQL concurrency test **nisu izvršeni u ovom prolazu**: nije dostupna SQL veza/Docker pipe. Opt-in SQL test bez `ZEV_TEST_SQL_CONNECTION` rano izlazi i ne predstavlja izvršenu SQL provjeru.
- Lokalni `docker compose ps` je odbijen na `npipe:////./pipe/docker_engine`; `dotnet-ef` nije instaliran. Nijedna postojeća baza/volume nije brisana ili resetovana.
- Frontend testovi su unit/static wiring, ne browser E2E. Nema UAT-a s pravim bankovnim fajlovima ni certifikacije PDF/uplatnice.
- Automatika i ručni workflow ne prelaze fondove; algoritam redoslijeda, gotovinski dokaz, djelimični povrat i vremenska politika čekaju poslovne odluke. Uplate sa nepoznatom osobom ne smiju se automatski pripisati vlasniku bez sigurnog pravila ili ručne identifikacije.
