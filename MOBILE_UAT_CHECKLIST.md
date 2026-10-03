# Mobile UAT — Android/iOS fizički uređaj

Status: **NOT EXECUTED**. `npm ci` i `npm run typecheck` prolaze; emulator/uređajski runtime nije provjeren. Test koristiti sintetičkim podacima i API-jem dostupnim telefonu preko HTTPS-a. Za svaki red zabilježiti uređaj, OS, build, datum, rezultat, screenshot/log i bug ID.

| Tok | Android | iOS | Napomena |
|---|---|---|---|
| Instalacija/Expo start i API povezivanje | NOT EXECUTED | NOT EXECUTED | Testirati online, offline i ponovni spoj. |
| Login, pogrešna lozinka, logout | NOT EXECUTED | NOT EXECUTED | Refresh tajna ostaje u SecureStore. |
| Admin reset i prisilna promjena lozinke | NOT EXECUTED | NOT EXECUTED | Samo ekran promjene, bez ZEV podataka; novi refresh token se čuva u SecureStore. |
| Moj profil i samostalna promjena lozinke | NOT EXECUTED | NOT EXECUTED | Traži trenutnu lozinku; stari tokeni prestaju raditi. |
| Istek access tokena, refresh rotacija, opozvan token | NOT EXECUTED | NOT EXECUTED | Nakon neuspjelog refresh-a tražiti ponovni login. |
| Lista ZEV-ova i izolacija korisnika | NOT EXECUTED | NOT EXECUTED | Drugi tenant ne smije biti prikazan ni API-jem. |
| Saldo, zaduženja, djelimična uplata, preplata | NOT EXECUTED | NOT EXECUTED | Uporediti s web/API karticom. |
| Uplatnica PDF download/share | NOT EXECUTED | NOT EXECUTED | PDF je informativan, ne službeni obrazac. |
| Dokumenti i privatni download | NOT EXECUTED | NOT EXECUTED | Testirati 403/404 za tuđi dokument. |
| Obavještenja i empty/loading/error stanja | NOT EXECUTED | NOT EXECUTED | Različiti scopes. |
| Prijava kvara s fotografijom, status promjena | NOT EXECUTED | NOT EXECUTED | Dozvole za kameru/fotografije i veličina fajla. |
| Glasanje i pokušaj duplog glasa | NOT EXECUTED | NOT EXECUTED | Uskladiti s pravno potvrđenom policy. |
| Background/foreground, restart aplikacije | NOT EXECUTED | NOT EXECUTED | Bez curenja tokena ili tuđih podataka. |
