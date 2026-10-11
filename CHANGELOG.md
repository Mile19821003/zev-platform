# Changelog

Sve značajne izmjene ZEV Platforme. Ranija historija (faze do 4) je u `CLOUD_HANDOFF_STATUS.md` i git logu.

## [v2.12] - 2026-10-11

### Dodano (Added)
- **Greške u sistemu** (Administracija i sistem → „Greške u sistemu“, samo sistemski administrator):
  - Globalni handler izuzetaka (`SystemErrorExceptionHandler`) bilježi svaku neobrađenu 500 grešku API-ja
    (ruta, tip, poruka, stack trace, korisnik) i vraća ProblemDetails „Interna greška servera.“ s `errorId`.
  - `POST /api/system/errors/client`: prijava greške iz web aplikacije (i bez prijave), rate limit 20/min po
    korisniku ili IP-u, polja se skraćuju; web šalje greške prikaza (React `ErrorBoundary` s ekranom „Došlo je do
    greške“) i neobrađene `error`/`unhandledrejection` događaje, uz dedupe i najviše 5 prijava u minuti.
  - `GET /api/system/errors?status=active|resolved|all` (stranice, najnovije prvo),
    `PUT /api/system/errors/{id}` (Završen + bilješka administratora; ponovno otvaranje briše datum završetka;
    audit `SystemErrorUpdated`).
  - Ekran: lijevo tabela ID / Naziv / Izvor (Frontend/Backend oznaka) / Greška / Vrijeme s filterima
    Aktuelni / Završeni / Svi; desno detalj s opisom, tehničkim detaljima, checkboxom „Završen“, bilješkom i „Snimi“.
- **Istorija verzija** (Administracija i sistem → „Istorija verzija“): `GET /api/system/versions` (numerički
  silazno, v2.12 → v2.7), `PUT /api/system/versions/{version}/notes` (audit `VersionNotesUpdated`); ekran s tabelom
  Verzija / Status / Naslov / Instalirano / Notes i dugmetom „Snimi note“ po redu.
- Zadane ikonice novih modula: `errors` → activity, `versions` → clipboard-check (mijenjaju se u „Prikazi i ikone“).
- Testovi: `SystemOperationsApiTests` (5), `web/tests/systemOps.test.mjs` (7); provjera u pregledniku 14/14.

### Ispravljeno (Fixed)
- Tip dokumenta zajednice: `ManagedDocumentType` dobija `InsurancePolicy` („Polisa osiguranja“) i `Decision`
  („Odluka“); forma „Novi dokument“ bira tip iz šifarnika `DOCUMENT_CATEGORY` (`LookupSelect`), tabela dokumenata
  prikazuje naziv iz šifarnika. Šifre šifarnika usklađene s enumom (ranije `INSURANCE_POLICY`/`DECISION` koje API
  nije prihvatao).

### Baza / Migracije
- `20261011055600_AddSystemErrorLogging`: tabela `SystemErrorLogs` (indeks Status + CreatedAt).
- `20261011055708_AddAppVersionHistory`: tabela `AppVersionHistory` (PK Version) sa seedom v2.7–v2.12.
- `20261011055745_AlignDocumentCategories`: `DOCUMENT_CATEGORY` šifre `INSURANCE_POLICY` → `InsurancePolicy`,
  `DECISION` → `Decision`; nove stavke Pravilnik (`Policy`), Statut (`Statute`), Pravni dokument (`Legal`),
  Ostalo (`Other`), tako da šifarnik pokriva sve vrijednosti enuma.

### Otvorene odluke (Open Decisions)
- Istorija verzija se puni seedom u migraciji; svaka nova verzija treba svoj red (migracija ili ručni unos) —
  automatsko upisivanje pri deployu nije uvedeno.
- Greške se čuvaju neograničeno; potrebno je pravilo zadržavanja/brisanja starih završenih grešaka.
- Prijava grešaka iz weba je anonimna (bez korisnika ako sesija ne postoji); da li tražiti prijavu ili dodati
  CAPTCHA/IP blokadu ako dođe do zloupotrebe.

## [v2.11] - 2026-10-10

### Dodano (Added)
- Forme čitaju opcije iz šifarnika (`GET /api/lookups/{typeCode}`):
  - Posebni dijelovi (radni prostor upravnika i sistemska administracija): tip iz `PROPERTY_UNIT_TYPE`; čuva se
    šifra (`Apartment`, `Commercial`, `Garage`, `ParkingSpace`, `Basement`, `Other`), tabela prikazuje naziv.
  - Rashodi: kategorija iz `EXPENSE_CATEGORY`.
  - Održavanje: kategorija prijave i plana iz `MAINTENANCE_CATEGORY`, prioritet iz `MAINTENANCE_PRIORITY`
    (zadano „Standardni“), tabela prijava prikazuje naziv prioriteta.
  - Ručna uplata: način plaćanja iz `PAYMENT_METHOD` (gotovina samo kad je dozvoljena podešavanjem).
  - Storno ručne uplate (novi dijalog „STORNIRAJ RUČNU UPLATU“ u detalju uplate, API iz v2.2) i ispravka
    raspoređivanja: razlog iz `REVERSAL_REASON` + opciona napomena („Dvostruka uplata: knjiženo dva puta“).
- `useLookup(typeCode)`: keš u memoriji za cijelu sesiju (jedan zahtjev po šifarniku za sve forme), fallback na
  sistemske opcije odmah (forma nikad ne čeka API) i kada API ne odgovori; keš se prazni kad administrator izmijeni
  šifarnik (dataSync ključ `lookups`, `/api/admin/...` sada objavljuje izmjene). Komponente `LookupSelect`,
  `LookupLabel`, `ReasonFields`; logika u `web/src/lookupOptions.ts`.
- Testovi: `web/tests/lookupOptions.test.mjs` (6, uključujući da je fallback jednak backend seedu), slučaj
  `ParkingSpace` u `ManagerRegistryCrudIsScopedAndInactiveMembershipIsDenied`; provjera u pregledniku 9/9.

### Ispravljeno (Fixed)
- `PropertyUnitType` dobija `ParkingSpace` (šifarnik ga je nudio, a API ga nije prihvatao); čuva se kao tekst,
  postojeći podaci nepromijenjeni.
- Postojeća vrijednost koja više nije ponuđena (deaktivirana stavka ili ranije upisan slobodan tekst, npr.
  kategorija „Rasvjeta“) ostaje izabrana i označena „(postojeća vrijednost)“ — uređivanje je ne mijenja tiho.

### Baza / Migracije
- `20261010213421_AddLookupCompatibilityItems`: stavke koje su forme već nudile — `PROPERTY_UNIT_TYPE` „Ostalo“
  (`Other`), `PAYMENT_METHOD` „Migraciona uplata“ (`Migration`) i „Drugo“ (`Other`).

### Otvorene odluke (Open Decisions)
- Kategorije rashoda i kvarova čuvaju **naziv** stavke (polja su slobodan tekst i postojeći zapisi/izvještaji
  ostaju čitljivi); preimenovanje stavke u šifarniku ne mijenja već snimljene zapise. Prelazak na šifre traži
  migraciju postojećih podataka.
- ~~Tip dokumenta zajednice (`ManagedDocumentType`) još koristi fiksnu listu; šifarnik `DOCUMENT_CATEGORY` ima
  stavke kojih nema u enumu (`INSURANCE_POLICY`, `DECISION`) — potrebna odluka o proširenju enuma.~~ Riješeno u v2.12.
- Nova šifra u `PROPERTY_UNIT_TYPE` ili `MAINTENANCE_PRIORITY` traži i proširenje enuma na backendu.
- SQL integracijski testovi: prvi prolaz nakon dužeg mirovanja SQL Server instance može imati timeoute u testovima
  performansi/konkurentnosti (isti testovi odmah zatim prolaze); zaostale testne baze iz prekinutih prolaza treba
  povremeno obrisati.

## [v2.10] - 2026-10-10

### Dodano (Added)
- Paleta poslovnih ikonica (`web/src/iconRegistry.ts`): 40 ikonica (Lucide stil) s ključem, nazivom i oznakama za
  pretragu — npr. `cog`, `sliders`, `barcode`, `clipboard-check`, `monitor`, `package`, `hard-hat`, `activity`,
  `shield-check`, `bot`, `file-lock`, `building`, `door`, `key`, `banknote`, `credit-card`, `landmark`, `receipt`,
  `chart-bar`, `chart-pie`, `workflow`, `palette`…
- `ModuleIconSetting` (modul → ikonica): migracija `AddModuleIconSettings` sa 34 početna reda (početna, 4 kategorije
  `category:*`, 29 modula oba radna prostora) — ikonice koje je navigacija koristila do sada.
  - `GET /api/system/module-icons` — svi prijavljeni: mapa `moduleKey → iconKey` i paleta.
  - `PUT /api/system/module-icons` — samo sistemski administrator (ostali 403): poznati moduli i ključevi iz palete
    (inače 400), bez duplikata; upis u `AuditLog` (`ModuleIconsUpdated`, prije/poslije samo za stvarne izmjene).
- Administracija → „Prikazi i ikone“ (`IconSettingsPage.tsx`): tabela Kategorija / Modul / Trenutna ikonica /
  Promijeni za sve kategorije i module; **Icon Picker** s mrežom palete i pretragom (bez obzira na dijakritiku);
  označene nesačuvane izmjene, „Odbaci“ i „Snimi izmjene“.
- Nadzorna ploča (kartice kategorija i modula, „Brzi pristup“) i lijevi meni (rašireni i sažeti 56 px) crtaju
  ikonicu iz podešavanja; nakon snimanja se odmah osvježavaju, bez F5 (dataSync ključ `module-icons`).
- Testovi: `ModuleIconApiTests` (3, uključujući SQL seed), `web/tests/moduleIcons.test.mjs` (5: paleta i početne
  ikonice identične backendu, pretraga pickera, renderovanje s podešenim ikonicama); provjera u pregledniku 13/13.

### Baza / Migracije
- `20261010211238_AddModuleIconSettings`: tabela `ModuleIconSettings` (jedinstven `ModuleKey`), seed 34 reda.

### Otvorene odluke (Open Decisions)
- Ikonice su globalne (cijela platforma); podešavanje po organizaciji ili korisniku nije predviđeno.
- Novi modul u navigaciji treba i red u `ModuleIconSettings` (migracija); dok ga nema, koristi se zadana ikonica
  iz `navigationModel.ts`.

## [v2.9] - 2026-10-10

### Dodano (Added)
- Generički šifarnici: `LookupType` (šifra, naziv, opis, `IsSystem`) i `LookupItem` (šifra, naziv, redoslijed,
  aktivna, zadana, opcionalno `ZevId` za stavku jednog ZEV-a).
  - Početni sistemski šifarnici: vrste posebnih dijelova, kategorije rashoda, kategorije kvarova, prioriteti
    održavanja, kategorije dokumenata, načini plaćanja, razlozi storna (31 stavka). Gdje u sistemu već postoji
    vrijednost, šifra stavke je ista (`Apartment`, `Urgent`, `Minutes`, `Cash`, `Transfer`…), pa se forme kasnije
    mogu direktno povezati.
  - `GET /api/lookups/{typeCode}[?zevId=]` — svi prijavljeni: samo aktivne stavke po redoslijedu (platformske +
    stavke tog ZEV-a; tuđi ZEV → 404).
  - `GET /api/admin/lookups`, `POST /api/admin/lookups/{typeCode}/items`, `PUT /api/admin/lookups/items/{id}`,
    `DELETE /api/admin/lookups/items/{id}` — samo sistemski administrator (ostali 403). Šifra se ne mijenja;
    deaktivacija umjesto brisanja (DELETE na stavci sistemskog šifarnika → 409); jedna zadana stavka po šifarniku
    (nova zadana uklanja prethodnu); neaktivna stavka ne može biti zadana; duplikat šifre → 409. Svaka izmjena
    ide u `AuditLog` (`LookupItemCreated` / `Updated` / `Deleted`).
- Web: Administracija → „Šifarnici“ (`LookupsPage.tsx`, logika u `lookupsUi.ts`): lijevo lista šifarnika s
  brojačem (aktivne/ukupno) i pretragom bez obzira na dijakritiku, desno tabela stavki (Šifra, Naziv, Redoslijed,
  Status, Akcije), modal za dodavanje i uređivanje s provjerom prije slanja, aktivacija/deaktivacija.
  Kartica „Šifarnici“ u kategoriji Administracija i u „Brzi pristup“ na početnom ekranu; stavka u lijevom meniju.
- Testovi: `LookupApiTests` (3), `SqlLookupTests` (seed i promjena zadane stavke na SQL Serveru),
  `web/tests/lookups.test.mjs` (5); provjera u pregledniku (simulirani API) 16/16.

### Baza / Migracije
- `20261010205359_AddGenericLookupSystem`: tabele `LookupTypes` i `LookupItems` (jedinstvena šifra po šifarniku i
  opsegu, filtrirani jedinstveni indeks za zadanu stavku, provjere), seed 7 šifarnika i 31 stavke.

### Otvorene odluke (Open Decisions)
- Postojeće forme (vrsta posebnog dijela, prioritet i kategorija kvara, kategorija rashoda, tip dokumenta, način
  ručne uplate) još koriste dosadašnje fiksne vrijednosti; povezivanje s `GET /api/lookups/...` je sljedeći korak.
  Za enum polja (npr. `PropertyUnitType`, `MaintenancePriority`) nova stavka u šifarniku traži i proširenje enuma
  ili prelazak polja na šifru.
- Upravljanje šifarnicima i stavke po ZEV-u za sada samo za sistemskog administratora (bez prava na nivou
  organizacije/ZEV-a).
- Novi šifarnici (tipovi) se dodaju samo migracijom; API ne kreira tipove.

## [v2.8] - 2026-10-10

### Dodano (Added)
- Sistemska podešavanja (regionalni prikaz): entitet `SystemSettings` (jedan red: `DecimalSeparator`, `DateFormat`,
  `FontSize`, `DefaultTheme`, `UpdatedAt`, `UpdatedByUserId`).
  - `GET /api/system/settings` — svaki prijavljeni korisnik (klijenti formatiraju prema njima); vraća i dozvoljene
    vrijednosti (`options`).
  - `PUT /api/system/settings` — samo sistemski administrator (ostali 403; pravo `System.Manage` ne postoji jer su
    detaljna prava vezana za ZEV, a podešavanja su globalna). Dozvoljene vrijednosti: separator `,` / `.`, datum
    `DD.MM.YYYY` / `YYYY-MM-DD`, font `13px` / `14px` / `15px`, tema `Default`; inače 400. Optimistička provjera
    preko `updatedAt` (409 za zastarjelu verziju). Promjena se upisuje u `AuditLog` (`SystemSettingsUpdated`,
    prije/poslije, autor).
- Web centralni formater `web/src/displaySettings.ts`: `formatNumber`, `formatMoney(amount, currency = "KM")`,
  `formatDate`, `formatDateTime` — bez `Intl`/`toLocaleString`, strogo prema odabranom separatoru i formatu.
  `useSystemSettings` učitava postavke nakon prijave, kešira ih u pregledniku i ponovo učitava nakon svake izmjene
  (dataSync ključ `settings`); veličina slova se primjenjuje na cijelu aplikaciju (osnovni font).
- Administracija → „Sistemska podešavanja“ (kategorija Administracija i sistem): izbor separatora, formata datuma i
  veličine slova, primjer prikaza i „Snimi podešavanja“; promjena se primjenjuje odmah, bez F5.
- Testovi: `SystemSettingsApiTests` (2: čitanje za sve prijavljene, izmjena samo za sistemskog administratora,
  400/409, audit), `web/tests/displaySettings.test.mjs` (4); provjera u pregledniku (simulirani API) 6/6.

### Ispravljeno (Fixed)
- Iznosi na webu više ne zavise od lokala preglednika (Edge je prikazivao `145.00 KM` umjesto `145,00 KM`):
  `currency.formatMoney`, `billingMoney`, kartica stanja računa, uplatnice/alokacije, troškovi održavanja i udjeli
  vlasništva koriste centralni formater. Vremena (`toLocaleString("bs-BA")`) u istoriji dokumenata i identifikacije
  uplatioca koriste `formatDateTime`.
- Datumi koji su već bili formatirani (kartica stanja računa, izvodi, obračun) prate odabrani format; `DD.MM.YYYY`
  se ispisuje bez završne tačke (`31.10.2026`).

### Baza / Migracije
- `20261010203655_AddSystemSettings`: tabela `SystemSettings` s provjerama dozvoljenih vrijednosti i jednog reda,
  FK na `Users`, početni red (`,`, `DD.MM.YYYY`, `13px`, `Default`).

### Otvorene odluke (Open Decisions)
- Dio tabela i dalje prikazuje datume kako ih API vraća (`2026-10-31`); prelazak svih prikaza na `formatDate`
  može se uraditi postepeno po ekranima.
- Mobilna aplikacija ima vlastiti formater (zarez, `DD.MM.YYYY.`) i još ne čita sistemska podešavanja.
- `DefaultTheme` ima samo vrijednost `Default` (temelj za kasnije teme).

## [v2.7] - 2026-10-10

### Dodano (Added)
- Nova navigacija radnog prostora (radni prostor uloga i sistemska administracija):
  - Moduli grupisani u 4 kategorije: **Finansije i računi**, **Registar i nekretnine**, **Zajednica i održavanje**,
    **Administracija i sistem** (`web/src/navigationModel.ts`).
  - Kontrolna ploča (`ModuleDashboard.tsx`) na stranici Pregled: velike kartice kategorija s ikonicom, opisom i
    brojem modula; klik otvara kartice modula s putanjom `← Nazad · Meni › Kategorija`.
  - Lijevi meni (`WorkspaceNav.tsx`): rašireni način (240 px) s grupama na harmoniku `+` / `−` i sažeti način
    samo s ikonicama (56 px) s tooltipom; dugme `‹` / `›` u zaglavlju menija. Izbor načina i otvorene grupe se
    pamte u `localStorage` (`zev.nav.collapsed`, `zev.nav.expanded`); grupa otvorenog modula je uvijek razvijena.
  - Prikaz kartica i stavki menija prati efektivna prava: kategorija bez dozvoljenog modula se ne prikazuje
    (npr. vlasnik vidi samo Finansije i računi te Zajednica i održavanje).
  - Nazivi prema novoj strukturi: „Obračun i računi“, „Uloge i matrica prava“, „Korisnici“, „Veze naloga“.
- Testovi: `web/tests/navigation.test.mjs` (4); provjera u pregledniku (simulirani API): 22/22 za radni prostor
  uloga (kartice, putanja, harmonika, sažimanje i pamćenje nakon reloada, prava vlasnika, overflow na 1440 i
  390 px) i 5/5 za sistemsku administraciju.

### Ispravljeno (Fixed)
- Mobilni prikaz (390 px): bočni meni se više ne razvlači preko širine ekrana — na mobilnom je harmonika
  (vertikalno) ili red ikonica koji se prelama; nema horizontalnog scrolla (zatvorena stavka iz backloga v2.6).

### Otvorene odluke (Open Decisions)
- Vizuelni predložak nije bio priložen; raspored je urađen prema tekstualnom opisu i može se uskladiti po
  pregledu (boje, ikonice, redoslijed modula unutar kategorija).

## [v2.6] - 2026-10-10

### Dodano (Added)
- Pravilo „403 ili 404“ za rute unutar ZEV-a, dokumentovano u `ACCESS_CONTROL_AND_PRIVACY.md`; helperi
  `TenantAccess.RequirePermissionAsync` / `RequireAllPermissionsAsync` / `DeniedAsync`.
- Web „Uplate i izvodi“ → „Bankovni račun“: dugme „Prikaži“ po računu i sekcija „Stanje računa i usklađivanje“
  (`BankAccountBalanceCard.tsx`, logika u `bankBalanceUi.ts`): stanje banke iz izvoda (iskazani saldo i datum,
  prilivi/odlivi, izvedeni saldo i neslaganje), stanje knjige fonda po izvoru, razlaganje razlike, status
  (usklađeno / razlika objašnjena / prekid u nizu izvoda / neobjašnjena razlika) i upozorenja o prekidima.
  Vidljivo uz `BankAccounts.View`.
- Tabela „Uvezeni izvodi“: kolone Period, Početni saldo, Završni saldo; pregled izvoda prikazuje saldo i period.
- `zev-platform.slnx` u korijenu (API + testovi): `dotnet build` / `dotnet test` rade iz korijena (ranije MSB1003).
- `MILESTONE_STATUS_v2.6.md`: stanje specifikacije, otvorene odluke i backlog za tim.
- Testovi: `AuthorizationPolicyTests` (2: 403 unutar ZEV-a, 404 za tuđi/nepostojeći ZEV),
  `web/tests/bankBalance.test.mjs` (3).

### Ispravljeno (Fixed)
- Rute unutar ZEV-a vraćaju **403** kada korisnik može čitati ZEV, a nema traženo pravo (ranije uglavnom 404,
  a samo 5 ruta ručnih uplata 403). Tuđi ili nepostojeći ZEV i dalje **404**. Pravo se provjerava prije
  postojanja resursa. Obuhvaćeno ~110 provjera u 14 endpoint fajlova (plaćanja, finansije, računi, storno/korekcije,
  rashodi, izvještaji, pravni predmeti, održavanje, registar, zajednica, glasanje, pristupi).
- `/access/overrides` i `/revoke`: delegiranje grubog prava koje dodjeljivač nema vraća 403 (ranije 404).
- Bez promjene (namjerno 404): tuđi lični resursi (kartica, račun, uplatnica, `PersonOnly`/`PropertyUnit`
  dokument, fajl, tuđa prijava kvara), zaštićene mete upravljanja pravima, rute organizacije i `/api/me`.
- Testovi koji su očekivali 404 za nedostajuće pravo ažurirani na 403 (12 provjera u 4 test fajla).

### Otvorene odluke (Open Decisions)
- Web `formatMoney` koristi `Intl` s lokalom `bs-BA`; preglednici bez tog lokala (npr. Edge u testnom okruženju)
  prikazuju `145.00 KM` umjesto `145,00 KM`. Postojeće ponašanje cijelog weba — za frontend polish (ručno
  formatiranje kao u mobilnoj aplikaciji).

## [v2.5] - 2026-10-10

### Dodano (Added)
- Saldo i period izvoda: `BankStatementImport.OpeningBalance`, `ClosingBalance`, `StatementFrom`, `StatementTo`.
  - CAMT.053: početni saldo `OPBD` (ili `PRCD`), završni `CLBD`, predznak iz `CdtDbtInd`, period iz `FrToDt`;
    valuta salda mora odgovarati valuti računa.
  - Interni XML: opcioni `<openingBalance>`, `<closingBalance>`, `<from>`, `<to>` pod korijenom.
  - CSV/TXT nemaju standardna polja salda — vrijednosti ostaju prazne (ništa se ne izvodi iz transakcija).
  - Prikaz u pregledu (`POST .../statements/preview`) i listi izvoda (`GET .../statements`).
- `GET /api/zevs/{zevId}/payments/accounts/{accountId}/balance` (pravo `BankAccounts.View`, inače 404):
  - **Banka** (samo iz izvoda): posljednji iskazani završni saldo i datum (`balanceSource` = `Statement` ili
    `NotProvided`), promet (prilivi, odlivi), izvedeni saldo = početni saldo najstarijeg izvoda + sav promet
    (samo ako je početni saldo poznat), lista izvoda i prekidi kontinuiteta (završni ≠ sljedeći početni).
  - **Fond** (samo iz knjige fonda, valuta računa): saldo i sastav — uplate s ovog računa, s drugih računa istog
    fonda, ručne uplate, rashodi, ostalo.
  - **Usklađivanje**: razlika promet banke − saldo fonda razložena na bankovne odlive koji nisu u fondu, ručne
    uplate i rashode koji nisu u banci i druge račune fonda; `unexplained` treba biti 0.
- Testovi: `BankAccountBalanceTests` (5: parseri, snimanje i odbijanje izvoda, razlaganje razlike, prekid
  kontinuiteta, izolacija po ZEV-u i prava) i `SqlBankAccountBalanceTests` (SQL Server).

### Ispravljeno (Fixed)
- Uvoz odbija izvod čiji se iskazani saldo ne slaže s transakcijama (početni + promet ≠ završni), izvod čija
  je valuta salda različita od valute računa i izvod čiji period završava prije početka.

### Baza / Migracije
- `20261010191157_BankStatementBalances`: kolone `OpeningBalance`, `ClosingBalance` (decimal(18,2)),
  `StatementFrom`, `StatementTo` (date) na `BankStatementImports`, check `CK_BankStatementImports_Period`.
  Postojeći izvodi ostaju bez salda (null).

### Otvorene odluke (Open Decisions)
- Bankovni odlivi (provizije, isplate dobavljačima) i dalje ne knjiže `FundTransaction`; izvještaj ih prikazuje
  kao „odlivi koji nisu u fondu“. Potrebna odluka da li ih vezati za rashode/knjigu fonda.
- Saldo za CSV/TXT izvode: banke ga ponekad daju u zaglavlju ili podnožju bez standarda — podrška po banci
  kad budu dostupni stvarni uzorci.
- Web prikaz salda računa (UI) još ne postoji (samo API).

## [v2.4] - 2026-10-10

### Dodano (Added)
- Matrica prava (`PermissionMatrix.tsx`): ćelije jasno razlikuju izvor prava — `✓` naslijeđeno iz uloge (zeleno),
  `✓+` ručni GRANT (plavo), `✕` ručni DENY (crveno), `□` nije dozvoljeno; tooltip navodi i šta bi uloga dala
  („uloga bi dozvolila“ / „uloga već dozvoljava“). Legenda iznad tabele.
- Nesačuvane promjene: isprekidan okvir ćelije, brojač „Nesačuvanih promjena: N“ i dugme „Odbaci nacrt“.
- Logika matrice izdvojena u `web/src/permissionMatrixUi.ts`; testovi `web/tests/permissionMatrix.test.mjs` (5).

### Ispravljeno (Fixed)
- Dva dugmeta istog ponašanja („Primijeni default uloge“, „Resetuj na default“) spojena u jedno:
  „Vrati sve na default uloge“ (u nacrt stavlja samo ključeve koji imaju ručni izuzetak).
- Kolona „Brisanje“ (nijedan modul nema akciju Delete) se više ne prikazuje; kolone se računaju iz modula,
  pa se pojavljuje automatski kad modul dobije tu akciju. Detaljni redovi koriste tačan `colSpan`.
- Klik na ćeliju: default → suprotan ručni izuzetak → nazad na default (ranije DENY → GRANT bez povratka na default).
- Snimanje šalje samo stvarne promjene (ranije i ključeve bez izmjene, što je moglo izazvati 403 provjere
  dodjeljivača za prava koja on nema); „Snimi prava“ je onemogućeno bez promjena.
- Poruka „Prava su sačuvana“ nestaje čim se nacrt ponovo mijenja; detaljni ključevi poravnati lijevo.

### Otvorene odluke (Open Decisions)
- Na mobilnom (390 px) stranica s 6+ stavki menija ima horizontalni scroll (bočni meni se ne prelama) — globalni
  raspored, postojao i prije; nije dio ove izmjene.

## [v2.3] - 2026-10-10

### Dodano (Added)
- Semantički audit promjena prava: `AuditLog.Action` za `AccessOverride` je `PermissionGranted`, `PermissionDenied`
  ili `PermissionRevoked` (umjesto `Added`/`Modified`), uz autora, vrijeme, korelaciju i razlog u JSON-u.
- Razlog opoziva (`AccessOverride.RevokeReason`): reset na default u matrici i zamjena postojećeg izuzetka čuvaju
  razlog iz zahtjeva; `GET /access/overrides` vraća `revokeReason`.
- Web (lista ručnih izuzetaka u `AccessControlPage`): polje „Razlog opoziva“, razlog prikazan uz status „Opozvano“.
- Testovi `AccessAuditTests` (5): audit događaji i razlozi, zaštita od eskalacije (403), zaštita vlastitih prava i
  administratora organizacije na svim rutama, obavezan razlog opoziva, append-only pravilo za izuzetke.

### Ispravljeno (Fixed)
- `POST /api/zevs/{zevId}/access/overrides` i `POST .../{id}/revoke`: korisnik (osim sistemskog administratora) više ne
  može mijenjati ni opozivati vlastita prava (404, kao i matrica).
- `POST .../{id}/revoke` traži razlog (`{ "reason": "..." }`, do 500 znakova, inače 400).
- Radni prostor sistemskog administratora: nova stavka menija „Pristupi i veze naloga“ ponovo otvara
  `AccessControlPage` (verifikacija veze nalog–osoba, izvedeni vlasnici, ručni izuzeci i opoziv), koji od `970dd2a`
  nije bio dostupan; „Uloge i pristupi“ i dalje otvara tabelarnu matricu (`ZevPermissionPage`).
- `AccessOverride` je append-only: dozvoljen je samo jednokratni opoziv (`RevokedUtc`, `RevokedByUserId`,
  `RevokeReason`); izmjena ostalih polja, ponovni opoziv ili brisanje se odbijaju.

### Baza / Migracije
- `20261010182929_AccessOverrideRevokeReason`: kolona `AccessOverrides.RevokeReason` (nvarchar(500)),
  check `CK_AccessOverrides_RevokeReason` (razlog samo uz opoziv).

### Otvorene odluke (Open Decisions)
- „Pristupi i veze naloga“ i dalje sadrži i matricu prava (ista kao „Uloge i pristupi“); razdvajanje u PR 5 po potrebi.
- `/revoke` opoziva samo grube ključeve (`zev.read`, `finance.manage`, …); detaljna prava se vraćaju kroz matricu.

## [v2.2] - 2026-10-10

### Dodano (Added)
- Storno ručne uplate: `POST /api/zevs/{zevId}/payments/{paymentId}/reverse` s tijelom `{ "reason": "..." }`.
  - Zahtijeva pravo `Payments.Reverse` (bez prava 403; bez pristupa ZEV-u 404) i obavezan razlog (do 500 znakova, inače 400).
  - Uplata dobija `Status = Reversed`, datum storna, razlog i autora (`ReversalReason`, `ReversedByUserId`).
  - Aktivne raspodjele se oslobađaju negativnim zapisima s istim razlogom (zaduženja ponovo postaju otvorena); već oslobođene se preskaču.
  - Knjiga fonda dobija `FundTransaction` tipa `Reversal` (−iznos, veza na originalnu uplatu), pa saldo fonda ostaje tačan.
  - Ponovljen zahtjev s istim razlogom je idempotentan (`alreadyExists: true`); drugačiji razlog ili bankovna uplata → 409 (bankovne uplate se storniraju povratom u izvodu).
  - Audit: promjena uplate i nove raspodjele se automatski upisuju u `AuditLog` (autor, vrijeme, razlog u before/after JSON-u).
- Testovi: `ManualPaymentReversalApiTests` (uspjeh, 403 bez prava / uz DENY, 404 bez pristupa, 400 bez razloga) i 4 servisna testa u `PaymentTests`.

### Ispravljeno (Fixed)
- Uplatnica po zaduženju oduzima kreditne korekcije i storno (`c050cc5`).

### Baza / Migracije
- `20261010181657_ManualPaymentReversal`: kolone `Payments.ReversalReason` (nvarchar(500)) i `Payments.ReversedByUserId`,
  check `CK_Payments_Reversal` (razlog samo uz status `Reversed`). Append-only pravilo dozvoljava ova polja samo
  jednom, zajedno s prelazom `Posted → Reversed`.

### Otvorene odluke (Open Decisions)
- Datum storna je dan izvršenja (UTC); retroaktivni datum storna nije podržan.
- Web UI za storno ručne uplate još ne postoji (samo API).
- Djelimični storno/povrat uplate nije podržan.
