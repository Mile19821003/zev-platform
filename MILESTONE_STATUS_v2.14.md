# ZEV Platforma — stanje projekta, milestone v2.14 (11.10.2026.)

Pregled za tim: šta je od milestone-a v2.6 doneseno u administraciji i sistemskim funkcijama (navigacija,
podešavanja, šifarnici, ikone, greške u sistemu, istorija verzija, obrisani redovi i teme), kako je pokriveno
testovima i šta ostaje kao otvorena odluka. Detalji po verzijama su u `CHANGELOG.md` (v2.7–v2.14); prethodni
presjek je `MILESTONE_STATUS_v2.6.md`.

## Stanje koda i testova

- Grana `main`; milestone obuhvata commitove od `9e9c15d` (v2.7) do commita s ovim dokumentom.
- `dotnet build` / `dotnet test` rade iz korijena repozitorija (`zev-platform.slnx`).
- Backend: build 0 upozorenja / 0 grešaka; **242/242** testova, uključujući SQL integracijske testove na
  lokalnom SQL Serveru (`ZEV_TEST_SQL_CONNECTION`, 13 fajlova). Na milestone-u v2.6: 220.
- Web: `npm run build --prefix web` PASS; **115/115** testova (na v2.6: 72).
- Mobilna aplikacija: bez izmjena u ovom ciklusu (posljednje stanje: Jest 33/33).
- Migracije: **40** ukupno, model i snapshot usklađeni (`dotnet ef migrations has-pending-model-changes`:
  bez izmjena). Seed redovi u migracijama navode tipove kolona (migracije nemaju Designer fajl).
- Provjere u pregledniku (Edge, simulirani API): PR 8 9/9, PR 9 14/14, PR 10 12/12, PR 11 17/17 — svaka u
  više uzastopnih prolaza.

### Koraci i PR-ovi ovog ciklusa

| Korak / PR | Verzija | Sadržaj | Commit |
|---|---|---|---|
| Korak 1 | v2.7 | Kontrolna ploča po kategorijama i sklopivi meni | `9e9c15d` |
| Korak 2 | v2.8 | Sistemska podešavanja (decimalni separator, format datuma, veličina fonta) | `7a9e656` |
| Korak 3 | v2.9 | Generički šifarnici (`LookupType` / `LookupItem`) i administracija | `e73d617` |
| Korak 4 | v2.10 | Paleta ikonica i podesive ikone modula | `c071d98` |
| PR 8 | v2.11 | Šifarnici u formama (`useLookup`), storno ručne uplate u UI-ju | `9f0b4d9` |
| PR 9 | v2.12 | Greške u sistemu, istorija verzija, kategorije dokumenata | `44a4958` |
| PR 10 | v2.13 | Obrisani redovi: soft-delete i vraćanje zapisa | `ddc3695` |
| PR 11 | v2.14 | Teme i paleta boja | `1c31017` |
| Čišćenje | v2.14 | Istorija verzija dopunjena s v2.13 i v2.14; ovaj dokument | (ovaj commit) |

## Sažetak PR 8 – PR 11

### PR 8 — Šifarnici u formama (v2.11)

| Zahtjev | Stanje | Dokaz |
|---|---|---|
| Forme čitaju opcije iz `GET /api/lookups/{typeCode}` (posebni dijelovi, rashodi, održavanje, ručna uplata, storno) | ✅ | `web/tests/lookupOptions.test.mjs`; browser 9/9 |
| Jedan zahtjev po šifarniku za cijelu sesiju; fallback odmah i kad API ne odgovori | ✅ | keš u `useLookup`; browser provjera broja zahtjeva |
| Fallback lista jednaka backend seedu | ✅ | test parsira `LookupEntities.cs` |
| Stara vrijednost koja više nije ponuđena ostaje izabrana („(postojeća vrijednost)“) | ✅ | `lookupOptions.test.mjs` |
| `PropertyUnitType.ParkingSpace` prihvaćen na API-ju | ✅ | `ManagerRegistryCrudIsScopedAndInactiveMembershipIsDenied` |

### PR 9 — Greške u sistemu, istorija verzija, kategorije dokumenata (v2.12)

| Zahtjev | Stanje | Dokaz |
|---|---|---|
| `SystemErrorLog`; neobrađeni izuzeci API-ja bilježe se i vraćaju `errorId` | ✅ | `UnhandledApiExceptionIsLoggedAndAnsweredWithErrorId` |
| `POST /api/system/errors/client` (i bez prijave, rate limit 20/min, skraćivanje polja) | ✅ | `ClientErrorsAreAcceptedEvenAnonymouslyAndClipped` |
| Web: `ErrorBoundary` + globalni `error` / `unhandledrejection`, dedupe, najviše 5 prijava u minuti | ✅ | `web/tests/systemOps.test.mjs`; browser |
| Ekran „Greške u sistemu“: filteri Aktuelni / Završeni / Svi, detalj, „Završen“, bilješka | ✅ | API testovi (403 za ne-administratora, audit); browser 14/14 |
| `AppVersionHistory` + ekran „Istorija verzija“ s bilješkama | ✅ | API test (numeričko sortiranje, audit); web test da je posljednja verzija u seedu jednaka najnovijoj u `CHANGELOG.md` |
| `ManagedDocumentType`: `InsurancePolicy`, `Decision`; forma dokumenata koristi `DOCUMENT_CATEGORY` | ✅ | API test (201 za nove tipove, 400 za stare šifre); web test enum ↔ šifarnik |

### PR 10 — Obrisani redovi (v2.13)

| Zahtjev | Stanje | Dokaz |
|---|---|---|
| Soft-delete (`IsDeleted`, `DeletedAt`, `DeletedByUserId`) za Zgrade, Ulaze, Posebne dijelove, Vlasnike/korisnike i Dobavljače; obrisani skriveni globalnim filterom | ✅ | `RecycleBinApiTests` |
| Brisanje samo bez živih zavisnih podataka (FK iz EF modela + Guid kolone po imenu); inače 409 s razlogom | ✅ | `RecordsInUseCannotBeDeletedAndRestoreNeedsLiveParent`, `DependencyCheckCoversForeignKeysAndNamedReferences` |
| `GET /api/admin/recycle-bin` (modul, forma, datum) i `POST /api/admin/recycle-bin/restore`, samo sistemski administrator | ✅ | `ManagerDeletesUnusedBuildingAndAdministratorRestoresIt` |
| Vraćanje provjerava roditelja i zauzetu šifru; filtrirani unique indeksi `[IsDeleted] = 0` | ✅ | `SqlRecycleBinTests` (SQL Server), `ReusedCodeBlocksRestoreAndOtherZevIsNotFound` |
| Audit `RecordDeleted` / `RecordRestored` | ✅ | API test |
| Ekran „Obrisani redovi“: filteri, „Osvježi“, „Vrati“ s toast obavijesti; „Obriši“ u registru i dobavljačima | ✅ | `web/tests/recycleBin.test.mjs`; browser 12/12 |

### PR 11 — Teme i paleta boja (v2.14)

| Zahtjev | Stanje | Dokaz |
|---|---|---|
| `SystemThemeSettings` (12 boja `#RRGGBB` + aktivna tema), seed = dosadašnje boje | ✅ | `ThemeApiTests`; seed provjeren na SQL Serveru |
| `GET /api/system/theme` (svi), `PUT` i `POST .../reset` (sistemski administrator, optimistička provjera, audit) | ✅ | `SystemAdministratorChangesAndResetsThePaletteWithAudit` |
| CSS varijable `--zev-*` u svim stilovima; tvornička tema izgleda identično | ✅ | `web/tests/theme.test.mjs`; browser: prijava, kontrolna ploča i tabela piksel-identične prethodnoj verziji |
| `useTheme`: primjena bez F5, keš (bez treptanja), osvježavanje nakon izmjene | ✅ | browser 17/17 |
| Ekran: picker i heks polje po boji, palete, pregled uživo, upozorenja o kontrastu, Snimi / Odbaci / Tvorničke | ✅ | web test kontrasta svih paleta; browser |

## Stabilnost

- Svi testovi zeleni na posljednjem prolazu (backend 242/242, web 115/115).
- SQL integracijski testovi su stabilni kada su lokalni `zev-platform-*` Docker kontejneri privremeno
  zaustavljeni; uz paralelni SQL u Dockeru prvi prolaz ponekad prekorači timeout (hladna instanca), ponovljeni
  prolaz uspijeva. Testne baze se brišu nakon svakog testa.
- Testovi su u ovom ciklusu prije commita otkrili i ispravili: hardkodirane kategorije dokumenata koje API
  nije prihvatao, nedovoljan kontrast u tamnoj paleti (3.7:1) i prepisivanje izmjena u editoru tema tokom
  učitavanja.

## Otvorene odluke

| Odluka | Izvor |
|---|---|
| Ukloniti ili spojiti `SystemSettings.DefaultTheme` s `SystemThemeSettings.ActiveThemeName` (dogovoreno za naredno čišćenje) | PR 11 |
| Statusne boje ostaju fiksne; tema je globalna (potvrđeno) | PR 11 |
| Nove verzije u istoriji verzija dodaju se migracijom; automatski upis pri deployu nije uveden (web test upozorava ako seed zaostaje za `CHANGELOG.md`) | PR 9 |
| Rok čuvanja grešaka u sistemu; zaštita anonimne prijave grešaka ako dođe do zloupotrebe | PR 9 |
| Planovi održavanja nisu soft-deletable (plan odmah generiše termine) — pravilo brisanja | PR 10 |
| Posebna prava `*.Delete` umjesto postojećih `*.Edit` / `Expenses.Create` | PR 10 |
| Trajno brisanje (purge) i rok čuvanja obrisanih zapisa | PR 10 |
| Kategorije rashoda i kvarova čuvaju naziv, ne šifru; prelazak traži migraciju podataka | PR 8 |
| Nova šifra u `PROPERTY_UNIT_TYPE` ili `MAINTENANCE_PRIORITY` traži i proširenje enuma | PR 8 |

## Sljedeći korak

Pauza s novim funkcionalnostima radi ručne verifikacije u pregledniku: navigacija i ikone, sistemska
podešavanja, šifarnici u formama, greške u sistemu, istorija verzija (v2.7–v2.14), brisanje i vraćanje zapisa
te teme (uključujući tamnu paletu i povratak na tvorničke boje).
