# Faza 7 — stanje implementacije

Historijski presjek; novi dokument UI gate i neodobrena pravila glasanja su u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Oglasna tabla sa vremenskim objavama za ZEV, zgradu, ulaz ili ulogu; server filtrira vidljivost.
- Centralni dokumenti sa tipom i vidljivošću za sve članove, pojedinačno lice ili samo upravnika; download provjerava isto pravo kao lista.
- Anketa i formalno glasanje sa jednim glasom po jedinici ili ponderom, konfigurabilnim kvorumom i osnovom potrebne većine.
- Pri otvaranju se snima lista podobnih vlasnika i težina; dupli glas po jedinici se odbija, a završeni rezultat i glasovi se zaključavaju.
- Migracija `Phase7CommunityVoting` primijenjena na lokalni SQL Server.

## Provjera

- Backend testovi: 27/27, uključuju autorizaciju privatnog dokumenta, konfigurabilni kvorum/većinu i duplo glasanje.
- Frontend build prolazi; postoje ekrani za objave, dokumente i glasanja.

## Ograničenja

- Dokumenti pojedinačnog lica su podržani u API-ju, ali UI trenutno dodaje samo dokumente za članove ili upravnika.
- Prilozi oglasa imaju bazu/API unos, ali nemaju UI download.
- Ponderisanje po površini ili koeficijentu je tehnička opcija; pravno pravilo i identitet glasa suvlasnika ostaju poslovna odluka.
- Zaključivanje glasanja je ručna radnja nakon isteka, bez automatskog pozadinskog servisa.
