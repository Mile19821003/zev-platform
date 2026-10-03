# Finansije V2 — Faza 2: storno, korekcije i lifecycle računa

Datum: 01.10.2026. Implementacija je nad postojećim `Zev.Api`/EF Core/SQL Server i React aplikacijama; Invoice V2 migracije `20260930180000` i `20260930190000` nisu mijenjane.

## Arhitektura i finansijski efekat

- Izvorni `Invoice`/`InvoiceLine` i izdani PDF ostaju snapshot. Samo `Invoice.DocumentStatus` prelazi `Issued → Cancelled` nakon storna ili `Issued → Corrected` nakon korekcije. `SaveChanges` odbija izolovanu promjenu statusa bez novog formalnog dokumenta u istoj transakciji. `Corrected → Cancelled` nije dozvoljen; dodatne korekcije jesu.
- `FinancialDocument` ima zaseban broj (`STO` ili `COR` niz po ZEV-u/godini), tip `Storno`/`Credit`/`Debit`, `DocumentStatus=Issued`, datum, razlog, napomenu, aktera, `RequestId` i snapshot ZEV-a, osobe, jedinice i izvornog broja. `FinancialDocumentLine` čuva potpisani iznos, opis/fond snapshot, novi `Charge` i opcionu referencu na izvorni `InvoiceLine`.
- Storno knjiži negativni `Charge` za svaku pozitivnu izvornu liniju i veže ga preko `ReversesChargeId`. Neplaćen račun: `+100 −100 = 0`, bez lažne uplate. Plaćen račun: originalni `Payment`/`BankTransaction` ostaje; negativni `PaymentAllocation` veže izvornu alokaciju i oslobađa puni plaćeni iznos kao neraspoređeni kredit. Djelimično plaćen račun oslobađa samo uplaćeni dio. Nema hard-delete niti izmjene originalnog iznosa.
- Credit knjiži negativne stavke uz kontrolu da kumulativno umanjenje ne premaši izvornu stavku; višak ranije alocirane uplate se oslobađa append-only negativnom alokacijom. Debit knjiži pozitivne stavke (uključujući novu stavku u fondu koji je već na računu); ne mijenja originalni `InvoiceLine`. Kredit se ne prenosi automatski na drugi fond ili dug, ali je vidljiv u Payments i može se ručno rasporediti u istom fondu i za istu osobu.
- `PaymentReconciliationEngine` ograničava novo povezivanje na efektivni kapacitet izvorne stavke nakon negativnih korekcija. Owner/unit ledger, otvorene stavke, lista računa i vlasnički portal čitaju potpisane finansijske redove. Korektivne stavke imaju `EffectiveOn` za `as-of` pogled; originalni period ostaje sačuvan.

## API, prava i UI

- `POST /api/zevs/{zevId}/finance/invoices/{invoiceId}/storno`: `Billing.Reverse`, razlog i idempotentni `requestId`.
- `POST /api/zevs/{zevId}/finance/invoices/{invoiceId}/corrections`: novo `Invoice.Correct`, `Credit`/`Debit` i 1–100 korektivnih stavki po zahtjevu.
- `GET /api/zevs/{zevId}/finance/invoices/{invoiceId}/documents` i `/timeline`; `GET /api/zevs/{zevId}/finance/documents/{documentId}` i `/{documentId}.pdf` koriste isti server-side owner/staff pristup kao originalni račun. Legacy projekcija nema formalni `InvoiceId` i ne može biti stornirana/korigovana.
- Admin UI prikazuje `IZDAT`/`STORNIRAN`/`KORIGOVAN`, izvorni i efektivni iznos, potvrdu nepovratnog storna, obavezan razlog, korekciju i povezane PDF-ove/timeline. Owner portal prikazuje povezane dokumente i preuzimanje PDF-a samo za vlastitu osobu. Preplata je u listi kredita s radnjom za ručno raspoređivanje. Mutacije pokreću postojeći tenant-scoped data-sync, bez obaveznog browser refresh-a.
- `SimplePdfWriter` s ugrađenim Unicode fontom generiše zasebni storno/korektivni PDF iz snapshot podataka. Originalni PDF ostaje byte-identičan nakon storna (API test). Formalni domaći sadržaj/poreski tretman nisu potvrđeni.

## Migracija i zaštita

- Nova aditivna migracija `20260930200000_InvoiceLifecycle`: tabele `FinancialDocuments`, `FinancialDocumentLines`; nullable kolone `Charges.FinancialDocumentId/ReversesChargeId/EffectiveOn` i `PaymentAllocations.FinancialDocumentId/ReversesAllocationId`; FK, unique/filtered indeksi i prošireni CHECK constraints za potpisane reversal redove. Postojeće tabele/kolone/redovi se ne brišu. Postojeća dva CHECK constrainta i jedan jedinstveni indeks se skidaju i vraćaju s novim uslovima; prije apply-a treba pregledati SQL na ciljnoj bazi.
- `IMigrator.GenerateScript` automatizovanim testom generiše SQL od `20260930190000` do `20260930200000` bez spajanja na bazu i provjerava da nema `DROP TABLE`, `DROP COLUMN` ili `DELETE FROM`. EF snapshot i runtime model su jednaki prema `IMigrationsModelDiffer` testu.
- SQL `Serializable` transakcija zaključava red ZEV-a (`UPDLOCK,HOLDLOCK`) prije broja i upisa; unique `(ZevId,RequestId)`, `(ZevId,Series,Year,Sequence)`, `(ZevId,DocumentNumber)` i filtrirani unique storno po izvornom računu štite dvostruki klik/paralelno knjiženje. `InvoiceNumberProvider` ima konfigurabilne različite prefikse `Finance:InvoiceNumberPrefix`, `Finance:StornoNumberPrefix`, `Finance:CorrectionNumberPrefix`. Audit bilježi novi dokument, linije, Charge, alokacijske protivstavke i promjenu originalnog statusa.

## Validacija i ograničenja

- Backend build: **PASS**, 0 warning / 0 error. Backend testovi: **PASS 130/130** (uključujući opt-in SQL testove koji bez veze izađu bez stvarnog SQL izvršenja). Dodati su servisi za 0/40/100% plaćeni storno i storno potpuno diskontovanog računa (0 KM), kredit, debit, više korekcija, zabranu duplog storna, idempotency, immutability, tenant/permission/owner PDF, izvještaj/ledger i migracijski SQL/snapshot.
- Frontend build/typecheck: **PASS**. Frontend testovi: **PASS 36/36** (prava/statusi, validacija razloga i korekcije, legacy zabrana, statička provjera UI wiring-a uz postojeće data-sync testove; nema browser/DOM automatizacije).
- **SQL INTEGRATION TESTS: NOT EXECUTED.** `ZEV_TEST_SQL_CONNECTION` nije postavljen; Docker API je u ovom sandboxu odbio pristup. Novi `SqlInvoiceLifecycleConcurrencyTests` je opt-in test koji kreira/uklanja samo zasebnu nasumično imenovanu testnu bazu. Njegov prolaz u ukupnom xUnit broju bez env varijable nije dokaz SQL concurrency-ja. Migracija **nije primijenjena** na postojeću niti produkcijsku bazu.
- Nije izdavan automatski zamjenski mjesečni račun poslije korekcije; kredit se ne refundira/raspoređuje automatski; korigovan račun se ne može stornirati; administrativna forma u jednom zahtjevu šalje jednu korektivnu stavku iako API podržava više. Službeni pravni obrazac storna/korekcije, numeracijska politika i poslovna vremenska zona čekaju odluku (`OPEN_BUSINESS_DECISIONS.md`, tačke 48–52). Tehnički datum izdavanja je trenutno UTC.

Za stvarno pokretanje SQL concurrency testa koristiti **isključivo izolovani testni SQL Server**:

```powershell
$env:ZEV_TEST_SQL_CONNECTION = 'Server=localhost,1433;Database=master;User Id=sa;Password=<TEST_ONLY_PASSWORD>;TrustServerCertificate=True'
dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --filter 'FullyQualifiedName~SqlInvoiceLifecycleConcurrencyTests'
```

Prije produkcijskog deploymenta: backup/restore drill, review generisane migracijske skripte, primjena u maintenance prozoru, SQL integracijski/concurrency test, UAT obračun/plaćanje/storno/korekcija, ovjera PDF sadržaja i monitoring.
