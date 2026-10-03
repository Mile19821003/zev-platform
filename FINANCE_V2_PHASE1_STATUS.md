# Finansije V2 — faza 1

Datum: 30.09.2026. Ovaj dokument dopunjava starije statusne dokumente, koji opisuju stanje prije trajnog izlaznog računa.

## Implementirano

- `Invoice` i `InvoiceLine` se kreiraju atomski s `BillingRun` i `Charge` u `MonthlyBillingEngine`. Jedinstveni indeksi štite period/jedinicu/osobu, poslovni broj i jedan `InvoiceLine` po `Charge`.
- `InvoiceNumber` je sekvenca po ZEV-u/godini; prefiks `Finance:InvoiceNumberPrefix` ima podrazumijevanu vrijednost `INV`. SQL knjiženje serijalizira konkurentne zahtjeve zaključavanjem ZEV reda; neuspjeh transakcije ne ostavlja djelimičan obračun.
- Svaki izdati dokument čuva snapshot ZEV-a, primaoca, jedinice, tarife, fonda, bankovnog računa, referencije, datuma i iznosa. PDF izdanog računa ne čita trenutne master podatke ni kasnije uplate. Aktuelno plaćeno/otvoreno dolazi iz `Charge` i aktivnih `PaymentAllocation`; neraspoređeni kredit je prikazan zasebno na detalju.
- Postojeći `BillingDraftAdjustment` podržava opis, jedinicu mjere, količinu, jediničnu cijenu i procentualni/fiksni popust uz preview/fingerprint. Za promjenu finansijskih vrijednosti traži razlog.
- Uplatnica pojedinačnog zaduženja i fonda koristi trenutno otvoreni iznos. Novi računi su u listi i vlasničkom portalu, stari `Charge` zapisi ostaju u legacy read-modeu bez izmišljenih snapshotova.
- EF audit evidentira nastanak dokumenta i stavki. Izdati `Invoice`/`InvoiceLine` su nepromjenjivi kroz EF. `Invoice.View`/`Invoice.Export` i postojeći owner/historical-person scope štite čitanje; knjiženje koristi `Billing.Post`.

## Migracija

`20260930180000_InvoiceV2` je aditivna: dodaje `Invoices` i `InvoiceLines`, FK, check i unique indekse. Ne mijenja niti briše postojeće `BillingRun`, `Charge`, `Payment` ili druge podatke. Snapshot modela je usklađen; test generisanja SQL skripte prolazi. Migracija **nije primijenjena na postojeću lokalnu/produkcijsku bazu u ovom radu**, jer Docker/SQL pristup u sandboxu nije dostupan. Prije pokretanja API-ja na postojećoj bazi napraviti backup i pregledati SQL skriptu. Ne koristiti `docker compose down -v` niti `EnsureDeleted` nad postojećom bazom.

## Validacija

- `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore`: prolazi.
- `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore`: 111/111 prolazi. Dva SQL opt-in testa se bez `ZEV_TEST_SQL_CONNECTION` tehnički računaju kao prolaz jer rano izlaze; SQL rezultat nije potvrđen.
- `npm run build --prefix web`: prolazi (TypeScript + Vite).
- Testovi pokrivaju snapshot i nepromjenjivost PDF-a, numeraciju/idempotentnost, vezu stavka–potraživanje, popust/zaokruživanje, suvlasnike, legacy prikaz, uplatnicu za djelimičnu uplatu i model/migracijsku skriptu. SQL opt-in performance/concurrency testovi se izvršavaju samo uz `ZEV_TEST_SQL_CONNECTION`; u ovom okruženju nije potvrđen njihov rezultat nakon ove nadogradnje.

## Ograničenja

- Storno, korektivni dokument i aktivno korištenje `Billing.Reverse`/`Invoice.Generate` nisu dio faze 1. `Draft` je enum vrijednost bez zasebnog draft dokument workflow-a.
- Legacy PDF ostaje informativan i dinamički; ne može se rekonstruisati historijski snapshot iz starih `Charge` zapisa.
- Jednostavni PDF generator je tehnički snapshot, nije odobreni zakonski obrazac, podržava samo ASCII transliteraciju i nema domaći QR. Pravno/računovodstveno odobrenje je otvoreno.
- Prethodni saldo snapshot ne raspoređuje neraspoređeni kredit osobe na pojedinačnu jedinicu. Kredit se vidi odvojeno u aktuelnom stanju.
- Lista kombinira nove račune i legacy stavke pa za velike periode učitava period u memoriju prije paginacije; potrebna SQL paginacija za veliki obim.
- Ako za fond nema aktivnog bankovnog računa, snapshot računa ne sadrži račun za uplatu tog fonda; uplatnica vraća konflikt.
- Istovremeni obračun fondova s različitim valutama se odbija bez konverzije. Podrazumijevana valuta bez aktivnog računa je `BAM` (`Finance:DefaultCurrency`), a službena numeracija/format zahtijevaju poslovnu odluku (`OPEN_BUSINESS_DECISIONS.md`).

## Pokretanje

Iz korijena projekta prvo podesiti tajne i SQL konekciju prema postojećem `README.md`/`compose.yaml`, napraviti backup i pregledati migracijsku SQL skriptu. API u Development režimu primjenjuje EF migracije pri startu; zato ga ne pokretati na produkciji bez odobrenog deployment postupka. Web build: `npm run build --prefix web`. Backend provjera: `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj`.
