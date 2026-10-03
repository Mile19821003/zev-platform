# Finansije V2 — Faza 2.1: lifecycle UX i stabilizacija

Datum: 01.10.2026. Faza 2 append-only model i prethodne migracije nisu mijenjani.

## Implementirano

- Timeline sada sabira alokacije iste uplate u jedan `Payment` događaj po računu. Negativne alokacije se sabiraju po uplati i finansijskom dokumentu u jedan `AllocationRelease` događaj. UI dodatno normalizuje duplikate prije prikaza; događaji imaju BHS nazive, datum/vrijeme i link na PDF dokumenta.
- Dokument i timeline vraćaju `actorName`. Za korisnika sa verifikovanom `Person` vezom u istoj organizaciji koristi se `Person.DisplayName`; inače `AppUser.Email`. Deaktivacija korisnika ne uklanja ni ime ni email iz read-modela. Nema snapshot/migracije; kasnija promjena imena može promijeniti prikaz ranijeg događaja, dok audit i actor ID ostaju trajni.
- Administratorska korekcija bira više originalnih stavki, podržava novu debitnu stavku u postojećem fondu, prikazuje potpisani ukupni iznos i šalje do 100 linija. Server zadržava provjeru iznosa, fonda, izvora i kumulativnog kredita.
- Stornirani račun primarno prikazuje `STORNIRAN`, a odvojeno izvorno plaćanje, otvoreno poslije storna, kredit nastao stornom i trenutno neraspoređeni kredit osobe. `InvoiceView` i owner-card izračunavaju te podatke iz postojećih alokacija/dokumenata, bez novog izvora salda.
- Korigovan račun prikazuje objašnjenje zabrane direktnog storna. Vlasnički portal razdvaja izvorni iznos, korekciju, efektivni iznos i stornirani saldo te nudi originalni i povezane PDF-ove.

## Validacija

- `dotnet build src/Zev.Api/Zev.Api.csproj --no-restore -v:q`: **PASS**, 0 warnings, 0 errors.
- `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --no-restore`: **PASS 133/133**. Novi testovi pokrivaju grupisanje Payment/AllocationRelease, dvije linije Credit/Debit i API actor/owner/cancelled summary. Ukupni broj uključuje opt-in SQL testove koji bez connection stringa rano izlaze.
- `npm run build --prefix web`: **PASS**.
- `npm run test --prefix web`: **PASS 45/45**. Novi testovi provjeravaju čiste UI funkcije i statičko povezivanje komponenti; browser E2E nije uveden.
- **SQL INTEGRATION TESTS: NOT EXECUTED.** `ZEV_TEST_SQL_CONNECTION` nije postavljen. SQL migracija Faze 2 nije primijenjena u ovom prolazu.
- **Migracija Faze 2.1: NE.** Nema promjena DB modela.

## Otvoreno

- `AppUser` nema zasebno ime/prezime. Ako nema verifikovanog `Person` profila, UI prikazuje email kao razumljiv identifikator. Prikaz nije historijski snapshot imena.
- Potrebni su SQL integracijski test timeline/read-model upita, stvarni browser/UAT pregled i ovjera službenog PDF sadržaja. Pravilo `Corrected → Storno` ostaje zabranjeno; poslovne odluke su u `OPEN_BUSINESS_DECISIONS.md`.
