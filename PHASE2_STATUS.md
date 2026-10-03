# Faza 2 — stanje implementacije

Historijski presjek. U završnom tehničkom prolazu dodat je manager-scoped registry CRUD, ownership UI i SQL 10k pagination test; najnoviji status je u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Administratorski meni i obrasci za organizacije, ZEV-ove, zgrade, ulaze, posebne dijelove, lica, korisničke naloge i uloge.
- Vremenska istorija vlasništva sa udjelima i provjerom preklapanja; odvojena evidencija korisnika i podstanara.
- Sistemski administrativni API sa server-side autorizacijom, validacijom i stranicama rezultata za glavne registre.
- Složeni strani ključevi sprečavaju povezivanje jedinice, vlasništva i lica iz različitih organizacija/ZEV-ova; rowversion štiti izmjene.
- Migracije `Phase2Registry` i `Phase2OwnershipRegistry` primijenjene na lokalni SQL Server bez brisanja podataka.

## Provjera

- Backend testovi: 10/10 prolazi, uključujući pet testova vremenske istorije vlasništva.
- Frontend `npm run build`: prolazi.
- Dodatna provjera kroz HTTP/UI nakon obnove API kontejnera još nije provedena; lokalna baza je ažurirana.

## Ograničenja

- UI pretraga referenci prikazuje najviše 100 rezultata; potrebna je unaprijeđena selekcija za velike registre.
- Naknadno je dodan upravnikov read-only pregled jedinica, lica i istorije vlasništva unutar dodijeljenog ZEV-a. Kreiranje/izmjena registra i dalje je kroz System Administrator UI.
- Nepotpuni zbir udjela je trenutno dopušten; pravilo potpune pokrivenosti čeka poslovnu odluku.
