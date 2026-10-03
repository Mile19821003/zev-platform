# Backup/restore drill — 27.09.2026.

## Izvršenje

**PASS za lokalni razvojni SQL Server preko TCP-a; Docker Engine nije bio dostupan sandboxu.** Pokrenut je opt-in test `SqlBackupDrillTests.BackupVerifyAndRestoreToSeparateDatabase` protiv baze `Zev`. Test je napravio `COPY_ONLY`/`CHECKSUM` backup `/var/opt/mssql/data/Zev_TestBackup.bak`, izvršio `RESTORE VERIFYONLY`, pročitao logička imena iz `RESTORE FILELISTONLY`, obnovio sadržaj u **novu** bazu `ZevRestoreTest`, provjerio najmanje osam EF migracija, najmanje 20 tabela i čitanje tabele `Zevs`, pa uklonio samo testnu bazu. Postojeća `Zev` baza nije resetovana ili obnovljena preko sebe. Test: 1/1 PASS, trajanje oko 0,9 s prema test runneru. Backup fajl ostaje u SQL data volume-u; premjestiti ga u namjenski šifrirani backup storage prije korištenja kao operativni backup.

Test zahtijeva eksplicitnu varijablu `ZEV_BACKUP_DRILL_CONNECTION` s `Database=Zev`; bez nje xUnit test se vraća bez izvođenja i **ne smije se prijaviti kao drill PASS**. Primjer u PowerShellu (tajna se ne ispisuje):

```powershell
$env:ZEV_BACKUP_DRILL_CONNECTION = 'Server=127.0.0.1,1433;Database=Zev;User Id=sa;Password=<LOKALNA_TAJNA>;Encrypt=False;TrustServerCertificate=True'
dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj --filter FullyQualifiedName~SqlBackupDrillTests
Remove-Item Env:ZEV_BACKUP_DRILL_CONNECTION
```

## Operativni postupak

1. Prije backup-a potvrditi ime ciljne baze, lokaciju backup volume-a, kapacitet, SQL prava i da restore cilj nije postojeća baza. Koristiti `COPY_ONLY, CHECKSUM` za vanredni drill.
2. Izvršiti `BACKUP DATABASE [Zev] TO DISK = N'/var/opt/mssql/backups/Zev_YYYYMMDD.bak' WITH COPY_ONLY, INIT, CHECKSUM;` pa `RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backups/Zev_YYYYMMDD.bak' WITH CHECKSUM;`.
3. `RESTORE FILELISTONLY` daje logička imena; zatim `RESTORE DATABASE [ZevRestoreTest] ... WITH MOVE ...` u zasebne `.mdf`/`.ldf` putanje. Provjeriti `__EFMigrationsHistory`, broj tabela, `DBCC CHECKDB([ZevRestoreTest])` i read konekciju. Nikad koristiti `REPLACE` nad postojećom `Zev` bazom.
4. Testnu bazu ukloniti tek nakon provjere. Backup kopirati izvan Docker hosta u šifrirano skladište, testirati čitljivost kopije i primijeniti retenciju/RPO/RTO prema odluci vlasnika projekta.

`docker compose down -v` nije pokrenut. Ovaj lokalni drill nije zamjena za off-host restore, `DBCC CHECKDB`, disaster-recovery vježbu i definisanu politiku retencije.
