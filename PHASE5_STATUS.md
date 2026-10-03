# Faza 5 — stanje implementacije

Historijski presjek; najnoviji status opomena, dugovanja i pravnih predmeta je u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Dobavljači i ulazni računi, odvojeni nacrt/knjiženje/storno. Fond se mijenja isključivo ledger stavkama; knjižene stavke su append-only.
- Pregled dospjelih dužnika iz zaduženja i raspodjela uplata.
- Konfigurabilna vremenska stopa zatezne kamate i broj dana obračunske godine; bez definisane stope obračun se odbija.
- Evidencija opomena sa snapshotom duga/kamate i pravnih predmeta, uključujući status, sud, advokata, naplaćeno i priloge.
- Lokalna file-storage apstrakcija sa kontrolom tipa/sadržaja i pristupom preko autorizovanog API-ja.
- Administratorski ekrani za rashode, dugovanja, opomene i predmete.
- Migracija `Phase5ExpensesLegal` primijenjena na lokalni SQL Server.

## Provjera

- Backend testovi: 21/21, uključuju knjiženje/storno rashoda, konfigurabilnu kamatu i upload validaciju.
- Frontend `npm run build` prolazi.

## Ograničenja

- Naknadno je dodat informativni PDF opomene na osnovu finansijskog snapshota. Pravni tekst, način dostave i tipografija nisu potvrđeni i dokument nije službeni obrazac.
- Nema posebnog UI-ja za dodavanje priloga pravnom predmetu, iako API i relacija postoje.
- Aging po kategorijama dana nije dovršen; API vraća najstarije dospijeće po dužniku.
- Obračun kamate je na trenutno otvorenu glavnicu po intervalu stope; obračun istorijskih djelimičnih uplata po danima zahtijeva dodatna pravila.

Naknadni acceptance audit korigira as-of filtriranje alokacija u pregledu dužnika i opomena. Potpuna historijska kamata i tekst opomene ostaju otvoreni.
