# Faza 8 — stanje implementacije

Historijski presjek; najnoviji status izvještaja, portala i mobilnog runtime-a je u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Izvještajni API za finansijski sažetak, otvorene stavke sa starošću i filterima, karticu vlasnika i godišnji pregled po mjesecima.
- XLSX izvoz otvorenih stavki i generički PDF godišnjeg pregleda, uz server-side provjeru finansijskih prava.
- Vlasnički API vezuje lice za prijavljeni korisnički nalog; privatna zaduženja i uplate drugih vlasnika se ne vraćaju.
- Web portal prikazuje vlastite nekretnine, zaduženja, uplate, saldo i PDF uplatnice; raniji ekrani pokrivaju kvarove, obavještenja, dokumente i glasanja.
- React Native/Expo osnova koristi postojeći API za prijavu, rotirajući refresh token u SecureStore, finansije, PDF uplatnicu, kvar s fotografijom, obavještenja, preuzimanje dokumenata i glasanje; posebni dio se bira na uređaju, a poslovna logika ostaje na serveru.
- Nove tabele nisu potrebne; nema migracije Faze 8.

## Provjera

- Backend 32/32 testa u završnoj provjeri, uključujući mobilni refresh, zabranu tuđih finansijskih podataka, PDF i XLSX strukturu te SQL performance test.
- Web build/typecheck i mobilni TypeScript typecheck prolaze.
- Mobilni prikaz nije ispitan na fizičkom uređaju ili emulatoru.

## Ograničenja

- Izvještajni filteri nisu kompletni za svaku vrstu izvještaja; nema objedinjene liste uplata/zaduženja kroz sve organizacije.
- Godišnji PDF i uplatnica su generički informativni obrasci; PDF trenutno pojednostavljuje dijakritiku, bez pravno/bankarski odobrenog dizajna ili QR-a.
- Mobilni klijent nema paginirano učitavanje svih zapisa ni test na fizičkom uređaju; preuzeti dokument se dijeli kroz sistemski dijalog i briše iz cache-a nakon završetka. Offline pristup i biometrija nisu definisani.
- Naknadni acceptance audit ograničava alokacije na `Payment.PaidOn <= to/asOf` i dodaje aging bucket-e; potpuni promet/početno stanje po proizvoljnom intervalu i storno uplata nisu implementirani.
