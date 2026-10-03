# Poslovne odluke za UAT — 27.09.2026.

Kategorije: **A** blokira produkciju, **B** blokira konkretnu funkciju, **C** može ostati konfigurabilno, **D** tehnički riješeno ali treba potvrdu. Kategorije su procjena rizika za planiranje, ne pravni savjet. Sva polja „Odluka korisnika” i „Datum odluke” ostaju otvorena; tehnički default nije usvojeno pravilo. ID odgovara `OPEN_BUSINESS_DECISIONS.md`. **12 odluka kategorije A.**

| ID | Tema | Trenutno stanje | Opcije | Preporučeni tehnički default | Da li blokira produkciju | Odluka korisnika | Datum odluke |
|---|---|---|---|---|---|---|---|
| 1 | Jurisdikcija/valuta/zaokruživanje | Otvoreno | Jedna/više valuta; pravila po jurisdikciji | Decimal(18,2), konfiguracija valute i rounding | A — da | — | — |
| 2 | Granica ZEV-a i JIB | Otvoreno | Jedna/više zgrada; obaveznost JIB-a | Više zgrada, JIB opcionalan | C — ne | — | — |
| 3 | Promjena vlasnika/suvlasništvo | Otvoreno | Mjesečni rez/razmjerno/ugovoreno | Snapshot osobe na zaduženju, bez retroaktivnosti | A — da | — | — |
| 4 | Tarife/dospijeće/porez/članovi | Otvoreno | Po fondu i periodu | Verzionisana tarifa, bez hardkodiranog poreza | A — da | — | — |
| 5 | Red alokacije/preplata | Otvoreno | Najstarije/fond/ručno | Jednoznačni poziv na broj, ostalo ručni red | A — da | — | — |
| 6 | Poziv na broj/QR/banke | Otvoreno | Bank-specific provider | Tačna referenca, QR isključen | B — bank/QR funkcija | — | — |
| 7 | Nevažeći poziv/povrat uplate | Otvoreno | Ručna provjera/storno | ManualReview, bez automatskog povrata | A — da | — | — |
| 8 | Kamata i rokovi | Otvoreno | Politika po jurisdikciji | Kamata se ne obračunava bez eksplicitne stope | A — da | — | — |
| 9 | Odobrenje vanrednog rashoda | Otvoreno | Jedno/dva odobrenja, prag | UAT samo ograničeni manager, produkcijski workflow čeka potvrdu | B — rashodi | — | — |
| 10 | Formalno glasanje | Otvoreno | Kvorum/većina/ponder | Vrijednosti se eksplicitno unose po glasanju | A — da | — | — |
| 11 | Privatnost/retencija | Otvoreno | Rokovi po tipu i jurisdikciji | Ne brisati audit bez odobrene politike | A — da | — | — |
| 12 | RPO/RTO/SLA/backup | Otvoreno | Definisati ciljeve i retenciju | Dnevni backup + periodični restore drill | A — da | — | — |
| 13 | Poziv i pristup bivšeg vlasnika | Otvoreno | Admin poziv/samoregistracija; auto opoziv | Samo admin dodjeljuje članstvo | A — da | — | — |
| 14 | Upravnik agregatni pristup | Otvoreno | Da/ne uz delegaciju | Samo eksplicitno dodijeljeni scope | C — ne | — | — |
| 15 | Nepotpuni vlasnički udjeli | Otvoreno | Dozvoli privremeno/zabrani | Privremeno dozvoli uz evidenciju | C — ne | — | — |
| 16 | Koeficijent po m² | Otvoreno | Uključi/isključi | Ne množiti bez posebne tarife | D — potvrda | — | — |
| 17 | ISO valuta i izvod bez valute | Otvoreno | Valuta računa/odbijanje | Valuta računa, odbiti eksplicitno različitu | D — potvrda | — | — |
| 18 | Domaći PDF/QR format | Otvoreno | Odobren provider | Informativni PDF, QR isključen | B — službena uplatnica | — | — |
| 19 | Datum/rounding kamate | Otvoreno | Različite konvencije | Konfigurabilna policy, bez zakonske stope | B — kamata | — | — |
| 20 | Rok/tekst/dostava opomene | Otvoreno | Odobren tekst i kanal | Informativna opomena bez pravnog statusa | A — da | — | — |
| 21 | Prava odobrenja rashoda/pravnog predmeta | Otvoreno | Jedno/više nivoa | Ograničen manager scope | B — approval | — | — |
| 22 | Glasačko tijelo | Otvoreno | Sve aktivne/upisane/povezane jedinice | Snapshot pri otvaranju glasanja | B — formalno glasanje | — | — |
| 23 | Pravo glasa suvlasnika | Otvoreno | Punomoć/saglasnost/pravilo | Ne tvrditi valjanost bez potvrde | B — formalno glasanje | — | — |
| 24 | Osnova pondera | Otvoreno | Površina/koeficijent/udio | Eksplicitan izbor pri kreiranju | C — ne | — | — |
| 25 | Službena uplatnica/Unicode/QR | Otvoreno | Ovjeren bankarski/platni standard | Samo informativni PDF | A — da | — | — |
| 26 | Kartica po periodu | Otvoreno | Promet/stanje na dan | Stanje na kraju perioda uz historijske alokacije | D — potvrda | — | — |
| 27 | Opseg izvještaja/retencija | Otvoreno | Limiti i rokovi | 10.000 redova XLSX open-items | C — ne | — | — |
| 28 | Mobilna sigurnost/offline/biometrija | Otvoreno | Online-only/biometrija/offline | Online-only, refresh u SecureStore | B — mobilni rollout | — | — |
