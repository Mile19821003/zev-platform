# Faza 9 — stanje implementacije

Historijski presjek. Noviji upload extension test, direktni API health smoke, četiri SQL race regresije i preostali sigurnosni gateovi navedeni su u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano i provjereno

- Endpointi za podatke drugih ZEV-ova, tuđe vlasničke finansije, dokumente i uplatnice vraćaju 404 uz server-side provjeru.
- Login/refresh imaju rate limit; lozinke su hashirane, refresh token je u bazi samo kao hash, JWT ima kratki rok i prava se čitaju iz baze.
- Finansijski ledger, zaduženja, alokacije, glasovi i audit su append-only u EF sloju; brisanje uplate, izvoda, bankovne transakcije ili knjiženog rashoda je odbijeno.
- Upload ograničava veličinu, MIME i magic bytes; Docker API dobija perzistentan storage volume. Docker Compose sada uključuje web servis.
- Namjenski SQL Server test kreira izolovanu bazu, primjenjuje sve migracije i dokazuje obračun 1001 zaduženja za manje od 30 sekundi.
- Acceptance prolaz nakon ove faze: 47/47 backend testova sa oba SQL opt-in testa aktivirana, web build, mobilni typecheck i EF provjera modela prolaze.
- `/health/live` i `/health/ready` ponovo su provjereni nakon zadnjih izmjena na lokalnom API-ju i bazi; administratorov login i `/api/me` prolaze.

## Ograničenja / prije produkcije

- Docker engine nije dostupan ovom sandboxu; `docker compose config` prolazi, a postojeći lokalni servis odgovara na health, ali novi Compose boot nije end-to-end potvrđen.
- Lokalni backup/restore drill sada prolazi (`BACKUP_RESTORE_DRILL.md`); off-host restore i mjerenje pod punim višekorisničkim opterećenjem nisu urađeni.
- Bankarski povrati, pravno odobreni QR/uplatnica, pravila suvlasništva i kamata ostaju otvorene poslovne odluke.
- PDF izvoz nije tipografski/Unicode produkcijski spreman; potrebno je certificirano formatiranje i font.
- Periodično auditirati zavisnosti i sigurnosna podešavanja u deploymentu; lokalni sandbox nije mogao dohvatiti NuGet vulnerability feed.
