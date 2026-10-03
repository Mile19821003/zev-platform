# Konačni status implementacije — 27.09.2026.

Ovo je historijski presjek; noviji završni tehnički audit, matrica i rezultat **63/63** uz aktivirane SQL opt-in testove su u `FINAL_TECHNICAL_CLOSURE.md`. Ostatak ovog dokumenta ostaje zapis stanja u vrijeme ranijeg prolaza, ne aktuelni produkcijski acceptance. Status nije Production Ready.

## Sažetak

Rad je nastavljen nad postojećom Fazom 1; nije zamijenjen projekt ni resetovana postojeća baza. Faze 2–7 imaju migrirane poslovne modele i radne API/web tokove. Faza 8 ima izvještajni API, vlasnički web portal i mobilnu osnovu. Faza 9 uključuje sigurnosne provjere, izolovani SQL performance test i operativne upute. **Cjelokupni produkcijski acceptance nije postignut**: nedostaju potvrđena poslovna/pravna pravila, kompletni mobilni tokovi i Docker end-to-end provjera.

## Implementirano po fazama

| Faza | Rezultat | Ograničenja |
|---|---|---|
| 2 | Organizacije, ZEV, zgrade/ulazi, jedinice, osobe, vlasništvo/korisnici, članstva, System Admin UI i upravnikov read-only registar | Upravnik nema CRUD registar; veći lookup-i ograničeni |
| 3 | Fondovi, verzije tarifa, mjesečni idempotentni obračun, zaduženja | Raspodjela duga kod promjene vlasnika u mjesecu nije pravno definisana |
| 4 | Više bankovnih parsera, deduplikacija izvoda, uplate/alokacije, automatsko/ručno sravnjenje, informativna PDF uplatnica | QR nema potvrđen standard; bankarski povrati traže poseban tok |
| 5 | Dobavljači, rashodi/knjiženje/storno, dugovanja, opomene s informativnim PDF-om, konfigurabilna kamata, pravni predmeti i datoteke | Službeni tekst opomene, puni aging i višestepena odobrenja nisu gotovi |
| 6 | Prijave kvarova, statusi, radni nalozi, prilozi, planovi održavanja/kalendarski API | UI za sve priloge/servisera i puni kalendar nisu gotovi |
| 7 | Objave, dokumenti s vidljivošću, ankete i zaključivo glasanje | Pravna pravila glasanja čekaju odluku; dio UI-ja nema priloženi download |
| 8 | Finansijski sažetak, otvorene stavke, kartica vlasnika, godišnji pregled, XLSX/PDF, vlasnički portal i Expo osnova s mobilnim downloadom | Nisu svi traženi filteri/izvozi/ekrani potpuni; mobilni uređaj nije testiran |
| 9 | Tenant/RBAC testovi, append-only kontrole, SQL performance test, Compose web/storage konfiguracija | Docker boot i backup/restore nisu verificirani, potreban prod hardening |

## Migracije

Na lokalnoj postojećoj bazi `Zev` primijenjene su:

1. `20260927055031_InitialIdentity`
2. `20260927112021_Phase2Registry`
3. `20260927113443_Phase2OwnershipRegistry`
4. `20260927114821_Phase3FinanceCore`
5. `20260927115603_Phase4Banking`
6. `20260927143920_Phase5ExpensesLegal`
7. `20260927150229_Phase6Maintenance`
8. `20260927153424_Phase7CommunityVoting`

Faze 8/9 ne mijenjaju shemu. `dotnet ef migrations has-pending-model-changes` potvrđuje da model i migracije odgovaraju; `database update` kaže da je baza ažurna.

## Provjera

- Backend: 32/32 automatizovana testa prolaze. Pokrivaju login, web/mobilni refresh i rotaciju, tenant/RBAC, vlasništvo i promjenu vlasnika, obračun osnova, verzije tarifa, bank import/duplikat, sravnjenje, djelimičnu uplatu i preplatu, rashod/storno, održavanje, dokument permission, glasanje, vlasničku finansijsku izolaciju, PDF/uplatnicu i XLSX.
- SQL Server: namjenski test je na izolovanoj bazi izvršio sve migracije i generisao 1001 zaduženje u manje od 30 s. Test se izvršava samo uz `ZEV_TEST_SQL_CONNECTION`; bez nje xUnit ga prikazuje kao prolaz iako se logički preskoči.
- Backend build, web `npm run build`, mobilni `npm run typecheck` prolaze. Mobilni runtime/emulator nije provjeren.
- Lokalni API je nakon zadnjih izmjena vratio 200 za `/health/live` i `/health/ready` (`database=up`), a administratorov login i `/api/me` prolaze. Kompletan Docker stack nije mogao biti pokrenut zbog odbijenog pristupa Docker Engine named pipe-u u sandboxu. `docker compose config --quiet` prolazi.
- NuGet vulnerability feed nije bio dostupan kroz lokalni TLS; zaseban dependency/security scan ostaje obavezan.

## Otvorena ograničenja i poslovne odluke

`OPEN_BUSINESS_DECISIONS.md` sadrži 28 pitanja o valuti/zaokruživanju, vlasništvu, tarifama, alokaciji, domaćem QR formatu, kamatama/opomenama, glasanju, privatnosti, backup RPO/RTO i mobilnoj sigurnosti. Nisu izmišljene zakonske stope ili kvorumi. Informativni PDF koristi jednostavan font i transliterira dijakritiku: ne koristiti ga kao službeni obrazac bez odobrenog providera. Nema potvrde 95% automatskog sravnjenja na stvarnom bankarskom uzorku; test sa 20/20 validnih jedinstvenih referenci je deterministički.

Otvoreni tehnički poslovi: produkcijski font/PDF i službeni tekst opomene, bank-specific CAMT detalji, povrati uplata, odobravanje rashoda, pun skup izvještajnih filtera, mobilna paginacija i uređajski test, pun Compose smoke, restore drill i višekorisnički load test. Ovo je razlog zašto svih 32 završnih acceptance tačaka nisu proglašene završenim.

## Lokalno pokretanje

Iz direktorija projekta, PowerShell:

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
# Urediti SA_PASSWORD, JWT_SIGNING_KEY i bootstrap nalog; WEB_ORIGIN/VITE_API_URL po potrebi.
docker compose up --build -d
docker compose ps
Invoke-WebRequest http://localhost:8080/health/live
Invoke-WebRequest http://localhost:8080/health/ready
Start-Process http://localhost:5173
```

Prvi startup može čekati SQL Server; API se restartuje nakon neuspjelog pokušaja. Nakon kreiranja admina obrisati `BOOTSTRAP_ADMIN_PASSWORD` iz `.env` i `docker compose up -d --force-recreate api`. Testovi: `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj`, `npm ci --prefix web`, `npm run build --prefix web`, `npm ci --prefix mobile`, `npm run typecheck --prefix mobile`. Za mobilni runtime postaviti `EXPO_PUBLIC_API_URL` na adresu API-ja dostupnu telefonu, zatim `cd mobile; npm start`.

## Produkcijski deployment

1. Potvrditi poslovna pitanja, prihvatiti migracije na staging kopiji i izvršiti UAT svih uloga. Testirati bankovni format, PDF i QR s relevantnom bankom/nadležnim tijelom.
2. Postaviti produkcijske tajne u secret store (`ConnectionStrings__Default`, `Jwt__SigningKey`, bootstrap samo prvi put). Obezbijediti TLS reverse proxy, precizan `Web__Origin` i `ReverseProxy__KnownProxies`, ne izlagati SQL port javno.
3. Napraviti i provjeriti backup. Primijeniti migracije kontrolisano komandom `dotnet ef database update --project src/Zev.Api --startup-project src/Zev.Api` s produkcijskom konekcijom. Ne koristiti razvojni automatski `MigrateAsync` za produkciju.
4. Pokrenuti API, statički web i trajni storage uz odgovarajuće dozvole; verificirati live/ready, login, cross-tenant zabrane i finansijski smoke. Uključiti TLS, metrike, centralizovane logove i alerting; testirati rollback i restore.
5. Ukloniti bootstrap lozinku, rotirati inicijalne tajne i periodično skenirati zavisnosti. Minimalno privilegirati SQL i backup račune.

## Backup i restore

- Backup: izvršiti SQL Server `BACKUP DATABASE [Zev] TO DISK = N'/var/opt/mssql/backups/Zev_YYYYMMDD.bak' WITH CHECKSUM, INIT;` putem `sqlcmd` sa tajnom iz secret store-a. Putanju backup-a montirati kao zaseban backup volume; kopiju šifrovati i premjestiti izvan hosta. Ne držati jedini backup u `sql-data` volumenu.
- Validacija: `RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backups/Zev_YYYYMMDD.bak' WITH CHECKSUM;` i `RESTORE FILELISTONLY ...` za logička imena fajlova.
- Restore drill na **izolovanoj** bazi/hostu: `RESTORE DATABASE [ZevRestoreTest] FROM DISK = N'...' WITH MOVE '<logical-data>' TO '<test-data-path>', MOVE '<logical-log>' TO '<test-log-path>', CHECKSUM, RECOVERY;`. Zamijeniti logička imena vrijednostima iz `FILELISTONLY`, zatim provjeriti integritet, migracije i health. Nikada ne prepisivati produkcijsku bazu bez odobrenog incident plana.
- Raspored, retencija, RPO/RTO i testna frekvencija čekaju poslovnu odluku br. 12.

## Inicijalna konfiguracija i sljedeći razvoj

Nakon bootstrap-a administrator kreira organizaciju, ZEV, zgradu/ulaz/jedinice, vlasnike, korisnike i članstva. Upravnik konfiguriše fondove, bankovne račune, verzije tarifa i kamatnu politiku prije obračuna/importa. Preporučeni prioriteti: odobrena lokalna pravila i obrazac, kompletan manager registry UI, korektan historijski saldo/aging, mobilni uređajski test, produkcijski Compose/backup drill i UAT.
