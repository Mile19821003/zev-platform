# Održavanje: prijave, radni nalozi, kalendar i izvještaji (v2.20)

Opis modula **Održavanje** za upravnike, predstavnike i stanare: od prijave kvara i periodičnog plana, preko radnog
naloga kao digitalnog dosijea, do godišnjeg izvještaja za skupštinu ZEV-a. Detalji izmjena su u `CHANGELOG.md`
(v2.16–v2.20), prava u `GRANULAR_PERMISSIONS.md`.

## Prijava kvara (stanar)

1. Stanar (verifikovani vlasnik ili korisnik stana) u **Moje prijave** bira „Gdje je kvar?“: **Moj stan** (bira stan)
   ili **Zajednički prostori** (upisuje mjesto, npr. „ulaz A, lift“), kategoriju, opis, prioritet i fotografiju.
2. Prijava odmah dobija status „Otvoreno“ i oznaku **Novo**. Uprava dobija e-mail na adresu iz
   *Sistemska podešavanja → E-mail za obavještenja* (ako je SMTP podešen na serveru; inače se poruka upisuje u log).
3. Zvonce 🔔 u zaglavlju pokazuje broj novih prijava aktivnog ZEV-a. „Označi pročitano“ skida oznaku.
4. Kada je kvar otklonjen (radni nalog završen), prijava je „Završeno“; stanar je može **Potvrditi**.

## Periodični plan i kalendar

- Plan (naslov, kategorija, interval u danima, prvi termin) kreira termine **60 dana unaprijed**; kalendar ih
  dopunjava kako vrijeme prolazi. Termini dalje od 60 dana prikazuju se kao **projekcija** (nisu snimljeni).
- Status termina: **Planirano** (datum u budućnosti), **Dospjelo** (danas ili ranije, nije urađeno), **U toku**
  (radni nalog u radu), **Završeno**, **Otkazano**.
- Filteri: Narednih 60 dana, Ovaj mjesec, Sljedeći mjesec, Tekući kvartal, Cijela godina, Proizvoljno (od–do),
  status i kategorija.
- Akcije: **Završeno** (interna provjera bez naloga), **Pokreni radni nalog**, **Dosije** (ako nalog postoji).
- Plan se može **deaktivirati** (zadržava istoriju) ili **obrisati** ako nema izvršenog rada; obrisani plan vraća
  sistemski administrator iz *Obrisani redovi*.

## Radni nalog — digitalni dosije

1. Nalog se pokreće iz termina u kalendaru (termin → „U toku“), iz prijave (prijava → „U radu“) ili samostalno.
   Za jedan termin postoji najviše jedan nalog.
2. **Izvođač**: dobavljač iz šifarnika ili vanjski izvođač (naziv, telefon).
3. **Prilozi**: račun / faktura (PDF), slike **prije** i **poslije**, izvještaj o radu — PDF, JPG ili PNG do 10 MB.
4. **Završi nalog**: datum završetka, opis izvršenog posla, broj računa, iznos (KM), fond (redovni / rezervni).
   Povezani termin i prijava automatski postaju „Završeno“. Opcija **Upiši trošak u rashode** kreira rashod kao
   **nacrt** (traži dobavljača iz šifarnika i aktivan fond); knjiži se u modulu Rashodi.
5. Završen nalog je **arhivirani dosije** — samo za čitanje, s galerijom priloga i revizijom (ko je i kada mijenjao
   status).

## Početni ekran

Kartica **„Održavanje: X dospjelih obaveza / Y u toku“** (Dospjelo / U toku / Uredno, broj novih prijava, sljedeći
termin) vodi direktno u kalendar filtriran na „Dospjelo“.

## Reporting centar (Izvještaji održavanja)

Na dnu modula, za odabranu godinu:

- **📋 Analitička tabela** — svi poslovi (planovi, prijave, nalozi): planirani i realizovani datum, status, izvođač,
  račun, iznos; filter po izvoru i pretraga. **Izvezi u PDF / Excel** za godišnju skupštinu (pravo
  `Maintenance.Export`; svaki izvoz se bilježi u audit).
- **📊 Grafikoni** — udio troškova po kategorijama (pita) i planirano naspram realizovanog po mjesecima (stupci).
- **📅 Gantogram** — sezonski prikaz 12 mjeseci po planu (lift, grijanje, krov, dezinsekcija…): završeni, dospjeli,
  planirani i projektovani termini.

## API pregled

| Ruta | Namjena |
|---|---|
| `POST /api/zevs/{zevId}/maintenance/tickets` | Prijava kvara (stan ili zajednički prostori) |
| `GET .../tickets/unread-count`, `POST .../tickets/{id}/mark-as-read` | Nove prijave (zvonce) |
| `GET .../calendar?from&to&status&category` | Kalendar s filterima i projekcijom |
| `POST .../work-orders` (`scheduleId` / `ticketId`) | Pokretanje radnog naloga |
| `GET .../work-orders/{id}` | Dosije |
| `POST / GET / DELETE .../work-orders/{id}/attachments[/{attachmentId}]` | Prilozi |
| `PUT .../work-orders/{id}/complete` | Završetak (termin, prijava, opcioni rashod) |
| `GET .../dashboard-summary` | Kartica na početnom ekranu |
| `GET .../reports/summary?year=` | Podaci Reporting centra |
| `GET .../reports/{year}.pdf`, `.../reports/{year}.xlsx` | Izvoz za skupštinu |
