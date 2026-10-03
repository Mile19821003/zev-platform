# Finansije V2 — Faza 3.2: kanonski owner balance

## Implementirano

- `OwnerCardService` je jedini izvor formula za `TotalCharges`, `TotalPayments`, `Open`, `Credit` i `NetBalance` vlasnika, uvijek grupisanih po valuti. `Charge`, `Payment` i append-only `PaymentAllocation` nisu mijenjani.
- Kredit osobe u JSON detalju izdatog računa sada dolazi iz iste kanonske projekcije, filtriran na valutu računa; više ne sabira kredit drugih valuta. PDF/uplatnica/ZIP ne izvršavaju ovu dodatnu owner-summary projekciju jer ne prikazuju trenutni kredit osobe.
- `/payments/owner-card/{personId}`, `/reports/owner-card/{personId}` i `/me/finance/{zevId}` koriste isti servis. Report/portal dodatno projektuju dokumente, ali ne računaju zaseban owner summary. Historijski pristup bivšeg vlasnika ostaje ograničen na vlastite jedinice/period vlasništva.
- Summary obuhvata kompletan skup; ledger je sortiran po `CreatedUtc, Id`, paginiran server-side i ima saldo po valuti. Otvaranje stranice uz `from` uključuje ranije događaje u početni saldo. Događaji razlikuju izdani račun, legacy zaduženje, uplatu, korekciju, storno, alokaciju i oslobađanje alokacije.
- `to=YYYY-MM-DD` je stanje na kraju dana po UTC-u za događaje s timestampom. `Payment.ReversedOn` je historijski `DateOnly`, pa intradnevna preciznost punog reversal-a nije dostupna.
- Web portal prikazuje zaduženo, uplaćeno, otvoreno, kredit i neto po valuti, te paginirani ledger. Finansijske stranice koriste `formatMoney(amount, currencyCode)`; BAM se prikazuje kao KM, EUR kao EUR. Zajednički numerički iznos više se ne prikazuje za miješane valute. Globalni sažetak za miješane valute vraća `mixedCurrency=true` i `null` umjesto lažnih suma; kombinovani godišnji izvještaj vraća 409.
- Billing preflight, preview i post jednako odbijaju odabrane fondove s više aktivnih valuta (`BILLING_MULTIPLE_CURRENCIES`). Web zahtijeva `creditPlanHash` kada je automatska primjena kredita uključena.

## API kompatibilnost

- **Non-breaking:** `balances`, `events`, `page`, `pageSize`, `totalCount` su zajednički; report/portal daju i `groupedByCurrency`. Stari skalarni `totalCharged`, `totalPaid`, `balance` ostaju samo za tačno jednu valutu; za više valuta su `null`.
- **Breaking:** stare report/portal `charges[]` i `payments[]` projekcije su uklonjene. One nisu bile paginirane niti su davale pouzdan owner balance. Klijenti trebaju koristiti `events[]` i `balances[]`. Ledger događaji sada nose `runningBalance`, `propertyUnitId` i `period`.
- `creditPlanHash` ostaje opcion na starom API POST-u radi kompatibilnosti. Novi web ga šalje/zahtijeva kada je auto-credit aktivan. Direktni klijenti bez hasha ne dobijaju preview/post fingerprint garanciju, iako server ponovo planira u transakciji.

## Testovi i ograničenja

- Regresija provjerava paritet tri endpointa za 100 BAM zaduženja/80 BAM uplate i 50 EUR zaduženja/uplate, alokaciju, kasniji reversal, korekciju, storno i kredit. Provjerava as-of 31.08/06.09, više od 1000 zaduženja, prvu stranicu i `totalCount`, te zabranu miješanog globalnog zbira.
- Test početnog i prethodnog paginiranog salda, frontend statički/component testovi za kartice, formatter, kredit i događaje, te test multi-currency billing preflight/preview/post su dodani. To nisu browser E2E testovi.
- Nema nove migracije: šema se ne mijenja. SQL-friendly grupisanje i `Count/Skip/Take` izbjegavaju N+1 po događaju; postoje indeksi za person/due date, payment `(ZevId, PersonId, PaidOn)` i allocation FK, ali `ORDER BY CreatedUtc, Id` preko tri izvora zahtijeva SQL plan/performance mjerenje prije dodatnog indeksa. InMemory test nije dokaz SQL Server translacije/performance.
- SQL integracijski testovi: **NOT EXECUTED** bez `ZEV_TEST_SQL_CONNECTION`. Stvarni browser, produkcijski podaci i migracija nisu testirani u ovom prolazu.
- Lokalna provjera: backend API i test projekt build **PASS** (0 upozorenja); backend testovi **162/162 PASS**; frontend build **PASS**; frontend testovi **54/54 PASS**. `dotnet build` bez argumenta nije primjenjiv jer repo nema `.sln`; korišteni su eksplicitni `.csproj` fajlovi.
- Invoice/document detalj je zasebna projekcija po dokumentu; njegov trenutni status i otvoreno po računu ne treba miješati s kanonskim owner summaryjem ili smatrati historijskim snapshotom statusa. Legacy Charge bez invoice snapshot-a uzima valutu iz aktivnog fondovskog bankovnog računa ili konfigurisanog defaulta; historijska valuta takvog reda nije nezavisno sačuvana.
- `/payments/unit-ledger/{unitId}` ostaje zasebna kartica jedinice (ne owner-balance endpoint) sa starim limitom 1000 i jednim `balance` iznosom; nije obuhvaćena konsolidacijom owner balance-a i ne treba je koristiti kao zbirni višestruko-valutni saldo vlasnika.

Faza 3.2 ne uvodi novu fazu, poslovno pravilo konverzije valuta niti promjene osnovnog ledgera.
