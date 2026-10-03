# SQL backup i restore

Lokalni testni drill i rezultat su u `BACKUP_RESTORE_DRILL.md`. To **ne potvrđuje** off-host oporavak, retenciju, šifriranje niti dogovoreni RPO/RTO.

## Backup (DBA, prilagoditi putanju i pristup)

```sql
BACKUP DATABASE [Zev]
TO DISK = N'/var/opt/mssql/backup/Zev_YYYYMMDD_HHMM.bak'
WITH INIT, CHECKSUM, COMPRESSION, STATS = 10;
RESTORE VERIFYONLY
FROM DISK = N'/var/opt/mssql/backup/Zev_YYYYMMDD_HHMM.bak'
WITH CHECKSUM;
```

Kopirati šifrirano izvan hosta, evidentirati checksum/retenciju i alarmirati neuspjeh. Čuvati odvojeno i file-storage volume; SQL backup sam ne sadrži uploadovane priloge. Dokumentovati konzistentan recovery point za oba spremišta.

## Restore drill

1. Na izolovanom SQL Serveru provjeriti backup sa `RESTORE VERIFYONLY` i pregledati logical file nazive sa `RESTORE FILELISTONLY`.
2. Vratiti u novu testnu bazu sa odgovarajućim `MOVE` putanjama; ne preko `Zev`.
3. Pokrenuti `DBCC CHECKDB`, pročitati `__EFMigrationsHistory`, broj ključnih tabela i sintetičke finansijske zapise.
4. Uporediti aplikacijski login i `/health/ready` protiv restore baze, te čitljivost zasebno vraćenih priloga.
5. Dokumentovati trajanje, RPO/RTO i odobrenje brisanja isključivo testne restore baze.

`ZEV_BACKUP_DRILL_CONNECTION` uključuje automatizovani lokalni test koji pravi backup, verificira i vraća u `ZevRestoreTest`, pa uklanja testnu bazu. Ne postavljati na produkciju bez DBA odobrenja.
