# Faza 6 — stanje implementacije

Historijski presjek; preostali kalendarski UI i plan workflow su u `FINAL_TECHNICAL_CLOSURE.md`.

## Implementirano

- Prijave kvarova sa kategorijom, opisom, lokacijom, prioritetom i fotografijom; vlasnik može prijaviti samo povezanu vlastitu jedinicu.
- Radni nalozi iz prijave ili samostalno, sa prioritetom, dobavljačem, rokom, troškom i prilozima.
- Kontrolisani prelazi statusa i istorija događaja za prijave i naloge.
- Planovi periodičnog održavanja, idempotentno generisanje budućih obaveza i kalendarski API.
- Navigacija i ekrani za upravnika, predstavnika i vlasnika uz server-side provjeru uloga.
- Migracija `Phase6Maintenance` primijenjena na lokalni SQL Server.

## Provjera

- Backend testovi: 24/24, uključuju izolaciju vlasnika, prava predstavnika, statuse i periodični raspored.
- Frontend `npm run build` prolazi.

## Ograničenja

- Kalendar trenutno prikazuje narednih 90 dana, bez interaktivnog mjesečnog prikaza.
- Dodjela servisera je preko dobavljača u API-ju; UI za izbor servisera u radnom nalogu nije dovršen.
- Dodavanje priloga postojećem nalogu postoji u API-ju, ali nema UI obrazac.
