# Produkcijski stack i operativna backup/restore procedura

Odnosi se na `compose.prod.yaml` (jedan Linux host s Dockerom). `compose.yaml` ostaje samo za lokalni razvoj.

| Cilj | Vrijednost |
|---|---|
| RPO | najviše 24 h — dnevni puni backup baze i file storage-a |
| RTO | do 60 min — potpuni oporavak na novom hostu (`scripts/restore-database.sh`) |
| Retencija | 7 dnevnih, 4 sedmične (nedjelja), 12 mjesečnih (1. u mjesecu) |
| Provjera | svaki backup: `CHECKSUM` + `RESTORE VERIFYONLY` + SHA-256; sedmično: automatski restore drill s `DBCC CHECKDB` |

## 1. Arhitektura

| Servis | Uloga | Pokretanje |
|---|---|---|
| `init-permissions` | jednokratno postavlja vlasništvo backup volumena (SQL Server radi kao uid 10001) | prije `db` |
| `db` | `mssql/server:2022-latest`, volumeni `zev_db_data` (baza) i `zev_db_backups` (backupi); bez izloženog porta; `sqlservr` kao PID 1 i `stop_grace_period: 60s` (uredno gašenje) | `healthcheck`: zdrav tek kad su sve baze `ONLINE` |
| `migrate` | jednokratno: kreira bazu ako ne postoji (`READ_COMMITTED_SNAPSHOT ON`, `RECOVERY SIMPLE`), primjenjuje idempotentnu EF migracijsku skriptu, održava SQL nalog aplikacije | nakon `db: service_healthy` |
| `api` | ASP.NET API, `Production`, radi kao `zev_app` (samo `db_datareader`/`db_datawriter`), volumen `zev_files` | nakon `db: service_healthy` **i** `migrate: service_completed_successfully` |
| `web` | SPA (nginx), isti domen kao API | — |
| `proxy` | Caddy: TLS terminacija, `/api/*` i `/health/*` → `api`, ostalo → `web` | nakon zdravih `api` i `web` |

API nikad ne starta prije baze i migracija, pa nema restart petlje dok SQL Server radi recovery ili dok se migracije primjenjuju.
`migrate` dodatno čeka dok se `Zev` stvarno može otvoriti (do 120 s), jer nakon pada servera baza može biti u recoveryju iako
server već prima prijave. Slika `mssql/server` pokreće `sqlservr` u pozadini iz bash skripte koja ne prosljeđuje `SIGTERM`;
zato `compose.prod.yaml` pokreće `sqlservr` direktno, inače bi svaki stop/redeploy završio s `SIGKILL` i crash recoveryjem.
Proxy ima fiksnu adresu (`ZEV_PROXY_IP`) koju API zna kao `ReverseProxy:KnownProxies`; bez toga API ne bi vjerovao
`X-Forwarded-Proto` i `UseHttpsRedirection` bi preusmjeravao u krug.

## 2. Prva instalacija

```bash
git clone https://github.com/Mile19821003/zev-platform.git /opt/zev && cd /opt/zev
cp .env.prod.example .env.prod && chmod 600 .env.prod     # popuniti tajne, ZEV_DOMAIN, ZEV_TLS, MSSQL_PID
docker compose -f compose.prod.yaml --env-file .env.prod up -d --build --wait
docker compose -f compose.prod.yaml --env-file .env.prod logs migrate   # "applied migrations 0 -> N"
curl -fsS https://$ZEV_DOMAIN/health/ready
```

Nakon prve prijave obrisati `BOOTSTRAP_ADMIN_*` iz `.env.prod`. DNS zapis domene i portovi 80/443 moraju biti dostupni
za Let's Encrypt (`ZEV_TLS=<e-mail>`); `ZEV_TLS=internal` daje samopotpisani certifikat i koristi se samo za testiranje.

## 3. Nova verzija (deployment)

1. Pregledati migracije koje ulaze: `docker compose -f compose.prod.yaml --env-file .env.prod run --rm migrate print > migrate-review.sql`.
2. Uzeti backup prije promjene: `scripts/backup.sh` (mora završiti s `BACKUP OK`).
3. `git pull && docker compose -f compose.prod.yaml --env-file .env.prod up -d --build --wait` — `migrate` se izvršava prije novog API-ja.
4. Provjeriti `/health/ready`, prijavu i jedan nefinansijski i jedan finansijski smoke (vidi `DEPLOYMENT.md`).

## 4. Dnevni backup

`scripts/backup.sh` (pokreće se iz root-a repozitorija; čita `.env.prod`):

1. Razred backupa: `monthly` 1. u mjesecu, `weekly` nedjeljom, inače `daily` (može se zadati argumentom).
2. `BACKUP DATABASE [Zev] ... WITH INIT, FORMAT, CHECKSUM, COMPRESSION` u `zev_db_backups:/db/Zev_<UTC>_<razred>.bak`
   (na Express izdanju kompresija nije podržana — skripta piše nekomprimovano i to bilježi).
3. `RESTORE VERIFYONLY ... WITH CHECKSUM`.
4. Arhiva file storage-a (`zev_files`) u `/files/Zev_<UTC>_<razred>.files.tar.gz` + provjera arhive.
5. SHA-256 za oba fajla u `/db/<set>.sha256`.
6. Rotacija po razredu (`ZEV_BACKUP_KEEP_DAILY/WEEKLY/MONTHLY`, podrazumijevano 7/4/12).
7. Off-host kopija (preporučeno obavezno):
   - `ZEV_OFFSITE_DIR=/mnt/zev-offsite` — montirani mrežni disk (NFS/SMB); set se kopira i provjerava po SHA-256;
   - `ZEV_OFFSITE_COMMAND='rclone copy "$ZEV_BACKUP_EXPORT" remote:zev-backups'` — bilo koji alat za object storage
     (`rclone`, `aws s3 cp --recursive`, `azcopy`); `$ZEV_BACKUP_EXPORT` je privremeni direktorij s provjerenim setom.
   Retenciju na off-host lokaciji podesiti istom shemom (lifecycle pravila bucketa ili rotacija na share-u) i šifrirati je.

Log: `logs/backup-<UTC>.log`; izlazni kod ≠ 0 znači neuspjeh. Raspored (root crontab ili systemd timer):

```cron
# /etc/cron.d/zev  — dnevni backup u 02:00, sedmični drill nedjeljom u 04:00
0 2 * * * root cd /opt/zev && ZEV_OFFSITE_DIR=/mnt/zev-offsite scripts/backup.sh >/dev/null 2>&1 || logger -t zev "backup FAILED"
0 4 * * 0 root cd /opt/zev && scripts/run-restore-drill.sh >/dev/null 2>&1 || logger -t zev "restore drill FAILED"
```

Alarmiranje: pratiti `logger` poruke ili izlazni kod iz monitoringa; nedostatak `BACKUP OK` u zadnjih 26 h je alarm.

## 5. Automatski restore drill

`scripts/run-restore-drill.sh [<set ili putanja do .bak>]` — bez argumenta uzima najnoviji backup iz volumena; putanja do
fajla na hostu služi za drill nad kopijom vraćenom s off-host lokacije.

1. Provjera SHA-256 zapisanog pri backupu (za setove iz volumena) i `RESTORE VERIFYONLY WITH CHECKSUM`.
2. Restore u privremenu bazu **`Zev_Drill`** na istoj instanci (`MOVE` na zasebne fajlove; produkcijska `Zev` se ne dira).
3. `DBCC CHECKDB ... WITH NO_INFOMSGS, ALL_ERRORMSGS` — bilo kakav izlaz je neuspjeh.
4. Integritet šeme: sve primijenjene migracije moraju postojati u kodu (inače FAIL), napomena ako je backup stariji od koda,
   provjera ključnih tabela i broj redova.
5. `Zev_Drill` se uvijek briše (i nakon greške); status `RESTORE DRILL PASS|FAIL` i trajanje u `logs/restore-drill-<UTC>.log`.

## 6. Oporavak na novom hostu (RTO ≤ 60 min)

1. Novi Linux host s Dockerom; klonirati repozitorij na istoj verziji (`git checkout <tag>`), vratiti `.env.prod` iz secret store-a.
2. Preuzeti zadnji set s off-host lokacije (`.bak`, `.files.tar.gz`, `.sha256`) i provjeriti: `sha256sum -c` (putanje u fajlu su `db/…` i `files/…`).
3. `scripts/restore-database.sh /restore/Zev_<UTC>_daily.bak /restore/Zev_<UTC>_daily.files.tar.gz`:
   pokreće `db`, odbija rad ako `Zev` već postoji, `RESTORE VERIFYONLY`, restore, `DBCC CHECKDB`, vraća file storage,
   zatim `up --wait` cijelog stacka — `migrate` ponovo veže `zev_app` login (novi server, novi SID) i primjenjuje novije migracije.
4. Preusmjeriti DNS na novi host; provjeriti prijavu, podatke i priloge prije otvaranja korisnicima.

Restore preko postojeće `Zev` baze se ne radi skriptom. Ako je potreban na istom hostu: zaustaviti `api`/`proxy`, uzeti backup postojećeg
stanja, preimenovati ili ukloniti bazu uz odobrenje, pa pokrenuti skriptu.

## 7. Verifikacija (10.10.2026., Docker Desktop, SQL Server 2022 Developer, mala testna baza)

| Provjera | Rezultat |
|---|---|
| Prvo pokretanje (`up --build --wait`) | 182 s s buildom; redoslijed `init-permissions` → `db` healthy → `migrate` (0 → 18 migracija) → `api` → `proxy`; 0 restartova |
| Ponovni deploy | 12 s; `migrate` idempotentan (18 → 18), `api`/`db` nisu restartovani |
| Restart / pad baze | 3× `docker kill` (crash recovery) + 3× čisti restart: svaki `up --wait` uspješan (17–31 s), `migrate` čeka recovery, API 0 restartova; gašenje SQL Servera 2–3 s s izlazom 0 (ranije `SIGKILL`, 137) |
| Proxy | HTTPS 200, `/health/ready` 200, HTTP → HTTPS redirect, HSTS; refresh cookie `Secure; HttpOnly; SameSite=Strict` radi kroz proxy |
| Najmanje privilegije | API sesije samo kao `zev_app` (`db_datareader`, `db_datawriter`, nije sysadmin); baza `RCSI=on`, `RECOVERY SIMPLE` |
| Backup | 11–20 s; kompresija 5.656 KB → 653 KB; `CHECKSUM` + `VERIFYONLY`; arhiva fajlova + SHA-256 |
| Rotacija | 10 dnevnih → 7, 6 sedmičnih → 4, 13 mjesečnih → 12 (najstariji uklonjeni sa svojim arhivama) |
| Off-host | kopija na direktorij prolazi `sha256sum -c`; `ZEV_OFFSITE_COMMAND` dobija kompletan set |
| Restore drill | PASS iz volumena (39 s) i iz off-host kopije (37 s): restore 3 s, `CHECKDB` bez grešaka, 18 migracija, 56 tabela; FAIL (izlaz 1) za oštećen set (SHA-256) i odsječen `.bak` (`VERIFYONLY`); `Zev_Drill` nikad ne ostaje |
| Oporavak na novom hostu | 61 s od praznog hosta (izgrađeni image-i) do zdravog stacka; prijava, podaci i prilog vraćeni; `zev_app` ponovo vezan; ponovni restore preko postojeće `Zev` odbijen |

Na stvarnom hostu RTO uključuje i build image-a (~3 min), preuzimanje seta s off-host lokacije i restore koji raste s veličinom
baze — redovni drill daje stvarno trajanje restore-a za praćenje u odnosu na cilj od 60 min.
