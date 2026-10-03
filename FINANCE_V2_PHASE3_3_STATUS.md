# Finansije V2 — Faza 3.3, finansijski read model

## Implementacija

- `FinancialReadModel` je zajednički sloj za UTC as-of granicu, historijsku valutu zaduženja, aktivne uplate/alokacije, otvoreno, kredit, efektivni iznos, grupisanje po valuti, status računa i otvorene stavke. Knjižni/write model nije mijenjan.
- `OwnerCardService` koristi taj sloj za vlasnika (`/payments/owner-card`, `/reports/owner-card`, `/me/finance`), `/payments/unit-ledger/{unitId}`, izdati račun i legacy projekciju. Summary obuhvata sve redove; događaji imaju server-side pagination i running balance po valuti. Neraspoređeni kredit pripada vlasniku, ne jedinici.
- Invoice V2 snapshot (`OriginalAmount`, `PreviousBalanceSnapshot`, `TotalDueAtIssue`, PDF podaci) ostaje nepromijenjen; `CurrentPaid`, `EffectiveTotal`, `TotalOpen`, storno kredit i korekcije dolaze iz zajedničkih finansijskih činjenica. Legacy račun ostaje jasno označen, bez izmišljanja Invoice V2 zapisa.
- Legacy `Charge` bez invoice/document snapshot-a dobija valutu samo ako historijske alokacije do posmatranog datuma imaju jednu valutu; inače `UNKNOWN`. Uplatnica za `UNKNOWN` ili valutu različitu od računa fonda vraća 409. Aktivni bankovni račun i `Finance:DefaultCurrency` nisu dokaz stare valute.
- Globalni `/reports/summary` i `/finance/funds` vraćaju iznose po valuti. Nesiguran scalar je `null`; godišnji kombinovani izvještaj odbija miješanu ili nepoznatu valutu. Rashodi i njihovi `FundTransaction` redovi nemaju pohranjenu valutu i označeni su `UNKNOWN`.
- `/reports/open-items`, `/expenses/debtors` i kreiranje opomene koriste isti open-item obračun; dužnici su odvojeni po valuti. Nova opomena se odbija za miješane/nepoznate dospjele stavke. Web prikazuje `UNKNOWN` kao `[valuta nepoznata]` i grupisane salde.

## API kompatibilnost

- `unit-ledger` zadržava stara scalar `opening`/`balance` polja samo kada postoji jedna valuta; u miješanom slučaju su `null`. Nova polja su `balancesByCurrency`, `openingByCurrency`, `page`, `pageSize`, `totalCount`; parametri `from`/`to` su UTC datumski filteri.
- Lista računa dodaje `currencyCode`; legacy više ne prikazuje BAM po defaultu. Fondovi dodaju `balancesByCurrency`; neprovjereni `balance` je `null`. Dužnici mogu imati više redova za istu osobu, po jednoj valuti.
- Nije dodana EF migracija. Nije mijenjan postojeći ledger write model niti su stari redovi backfillovani.

## Regresije i ograničenja

- Završna provjera: `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore` PASS (0 warning/error); `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore` PASS 167/167; `npm run build --prefix web` PASS; `npm run test --prefix web` PASS 58/58. SQL INTEGRATION TESTS: NOT EXECUTED.
- In-memory/API regresije pokrivaju vlasnik–jedinica–invoice–report–portal parity, Credit −10 i allocation reversal −20, storno 100/100 s oslobođenim kreditom, BAM/EUR izolaciju, legacy `UNKNOWN`, as-of prije/poslije reversal-a, fondovski povrat i 1001+ zaduženja s paginiranim unit ledgerom. Frontend testovi pokrivaju formatter, legacy label i grupisani prikaz.
- `ToQueryString()` provjerava SQL Server translaciju glavnih charge/open-item/fund agregata bez spajanja na bazu. SQL integracijski testovi i stvarni planovi izvršavanja nisu provjereni jer `ZEV_TEST_SQL_CONNECTION` nije postavljen; potrebna je provjera svih složenih projekcija, indeksa i vremena izvršavanja prije produkcije.
- Historijski rashodi, opomene i pravni predmeti nemaju currency snapshot; UI ih označava nepoznatim. Za trajno rješenje potrebna je poslovna odluka i buduća additive migracija, bez nagađanja starog BAM-a. Stari godišnji scalar report je blokiran ako postoje nepoznate/miješane valute.
- Lista računa još prvo čita sve izdane i legacy redove za period, pa paginira u memoriji; to ne kvari finansijski total, ali traži SQL performance/UAT i kasniju server-side optimizaciju.
