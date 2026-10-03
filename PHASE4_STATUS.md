# Faza 4 — stanje implementacije

Historijski presjek; povrat/reversal bankarske transakcije još je otvoren u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Bankovni računi vezani za ZEV, fond i valutu.
- Provider/parser za CSV, TXT, generički XML i CAMT.053; kontrola veličine, strukture, valute, SHA-256 duplikata i status importovanih transakcija.
- Uplate, raspodjele na zaduženja i finansijske transakcije fonda; djelimična uplata i preplata ostaju tačno iskazane.
- Determinističko sravnjenje po identičnom pozivu na broj, evidencija pravila/rezultata i red za ručni pregled.
- Administratorski ekran za račune, import i ručnu raspodjelu; kartica vlasnika dostupna putem API-ja.
- Migracija `Phase4Banking` primijenjena na lokalni SQL Server.

## Provjera

- Backend testovi nakon faze: 18/18, uključuju parsere, dupli import, 20/20 ispravnih referenci, djelimičnu uplatu, preplatu i ručno sravnjenje.
- Frontend build prolazi. Lokalni API prolazi login i registracione smoke provjere.

## Ograničenja

- Naknadno je dodat `IPaymentSlipProvider` i generička PDF uplatnica; `IPaymentQrProvider` je zamjenjiva strategija bez podrazumijevanog QR standarda. Uplatnica je informativna, ne potvrđeni bankarski obrazac.
- CAMT.053 parser pokriva osnovne `Ntry` podatke; specifični detalji banke i više transfera unutar jedne stavke zahtijevaju profil banke.
- Samo pozitivne bankovne transakcije postaju uplate; povrati i izlazne transakcije idu na ručni pregled.
- UI ručnog sravnjenja trenutno podržava pretragu zaduženja po tačnom pozivu na broj.

Naknadni acceptance audit dodaje read-only preview izvoda prije knjiženja, s provjerom tenant-a, valute i duplikata te regresijskim testom. Povrat uplate i stvarni bankarski uzorci i dalje nisu riješeni.
