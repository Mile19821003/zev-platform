# Faza 3 — stanje implementacije

Historijski presjek; najnoviji tehnički i produkcijski gateovi su u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Redovni i rezervni fond po ZEV-u. Stanje se računa iz transakcija fonda, bez ručno održavane kolone salda.
- Verzije tarifa sa osnovama po m², po jedinici, po članu i vanredno; nova verzija zatvara vremenski interval stare.
- Mjesečni obračun sa serijskim transakcijama, jedinstvenim obračunom za ZEV/period, snapshotom tarife i vlasničkog udjela na zaduženju.
- API i administratorski ekran za fondove, tarife, obračun i listu zaduženja.
- Migracija `Phase3FinanceCore` primijenjena na lokalni SQL Server.

## Provjera

- Backend testovi nakon faze: 13/13. Test za 1001 zaduženje prolazi u EF InMemory, ali nije SQL Server performance potvrda.
- `npm run build` prolazi; lokalni `/health/live` i `/health/ready` vraćaju HTTP 200.

## Ograničenja

- Kartica vlasnika ima API u Fazi 4, ali još nema poseban korisnički ekran.
- Obračun koristi stanje vlasništva na prvi dan perioda; raspodjela kod promjene vlasnika usred mjeseca ostaje otvorena poslovna odluka.
- Referenca zaduženja je interni jedinstveni identifikator, ne potvrđeni domaći format poziva na broj.
- Naknadni namjenski SQL Server test na izolovanoj bazi potvrđuje 1001 zaduženje za manje od 30 s.
