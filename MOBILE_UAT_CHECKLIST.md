# Mobile UAT — Android/iOS fizički uređaj

Status: **NOT EXECUTED na uređaju**. Verifikacija koda (Faza 4, 10.10.2026.): Jest 33/33, `npm run typecheck` PASS,
`npx expo export` Android/iOS PASS, expo-doctor 21/21. Emulator se na razvojnoj mašini ne koristi; UAT se radi na
fizičkom telefonu preko Expo Go. Test koristiti sintetičkim podacima i API-jem dostupnim telefonu preko HTTPS-a.
Za svaki red zabilježiti uređaj, OS, verziju Expo Go, datum, rezultat, screenshot/log i bug ID.

## Povezivanje fizičkog uređaja preko Expo Go

1. **Telefon:** instalirati **Expo Go** iz Google Play / App Store. Verzija mora podržavati Expo SDK 57
   (Expo Go uvijek podržava samo aktuelni SDK; ako prijavi nekompatibilan SDK, ažurirati Expo Go).
2. **Mreža:** telefon i računar na istoj Wi-Fi mreži (bez "client isolation"). Ako to nije moguće, koristiti tunel
   (korak 5).
3. **API adresa dostupna telefonu:** `localhost` na telefonu je sam telefon, zato se navodi IP ili domen računara/servera.
   - Preporučeno: stvarni HTTPS domen test servera (`compose.prod.yaml`, Caddy s javnim certifikatom), npr.
     `EXPO_PUBLIC_API_URL=https://zev-test.example.ba`.
   - Lokalni stack (`ZEV_TLS=internal`) koristi Caddy interni CA kojem telefon ne vjeruje — HTTPS zahtjevi padaju.
     Za brzi test u razvojnom režimu (`__DEV__`) dozvoljen je i HTTP, npr. `EXPO_PUBLIC_API_URL=http://192.168.1.50:8080`
     prema API-ju objavljenom na LAN-u (otvoriti port u Windows firewallu). Produkcijski build odbija HTTP.
4. **Pokretanje (na računaru):**
   ```powershell
   cd mobile
   npm ci
   $env:EXPO_PUBLIC_API_URL = "https://zev-test.example.ba"
   npx expo start
   ```
   Skenirati QR kod: Android — iz Expo Go aplikacije; iOS — kamerom telefona.
5. **Ako se telefon ne može spojiti na Metro** (različite mreže, firewall): `npx expo start --tunnel`.
6. **Prije testa:** `npm test` i `npm run typecheck` moraju proći. Nakon izmjene `EXPO_PUBLIC_API_URL` restartovati
   `npx expo start` (vrijednost se ugrađuje u bundle), po potrebi s `--clear`.
7. **Offline test:** nakon uspješne prijave i učitavanja kartice uključiti avionski režim i restartovati aplikaciju —
   očekuje se baner "Bez veze sa serverom · podaci od …" i kartica samo za čitanje; isključiti avionski režim i
   "Pokušaj ponovo".

Napomene: refresh token je u `expo-secure-store` (Keychain/Keystore); offline snimak (profil, ZEV lista, kartica) je
u sandboxu aplikacije bez dodatnog šifrovanja (odluka 10.10.2026.) i briše se pri odjavi. Biometrija nije
implementirana (kasnije).

## Tokovi

| Tok | Android | iOS | Napomena |
|---|---|---|---|
| Instalacija/Expo start i API povezivanje | NOT EXECUTED | NOT EXECUTED | Testirati online, offline i ponovni spoj. |
| Login, pogrešna lozinka, logout | NOT EXECUTED | NOT EXECUTED | Refresh tajna ostaje u SecureStore; logout briše i offline snimak. |
| Admin reset i prisilna promjena lozinke | NOT EXECUTED | NOT EXECUTED | Samo ekran promjene, bez ZEV podataka; novi refresh token se čuva u SecureStore. |
| Moj profil i samostalna promjena lozinke | NOT EXECUTED | NOT EXECUTED | Traži trenutnu lozinku; stari tokeni prestaju raditi. |
| Istek access tokena, refresh rotacija, opozvan token | NOT EXECUTED | NOT EXECUTED | Nakon odbijenog refresh-a (401) tražiti ponovni login; greška servera/mreže ne briše token. |
| Lista ZEV-ova i izolacija korisnika | NOT EXECUTED | NOT EXECUTED | Drugi tenant ne smije biti prikazan ni API-jem. |
| Saldo po valuti, računi, djelimična uplata, preplata | NOT EXECUTED | NOT EXECUTED | Uporediti s web karticom vlasnika; iznosi u formatu `1.234,50 KM`, statusi na bosanskom. |
| Uplatnica PDF download/share | NOT EXECUTED | NOT EXECUTED | Samo za otvorene, nestornirane račune; iznos = otvoreni iznos nakon korekcija, 12-cifreni poziv na broj. PDF je informativan. |
| Offline režim (avionski režim, restart, ponovni spoj) | NOT EXECUTED | NOT EXECUTED | Baner s vremenom snimka; akcije (kvar, glasanje, dokumenti, uplatnica) skrivene. |
| Dokumenti i privatni download | NOT EXECUTED | NOT EXECUTED | Testirati 403/404 za tuđi dokument. |
| Obavještenja i empty/loading/error stanja | NOT EXECUTED | NOT EXECUTED | Različiti scopes. |
| Prijava kvara s fotografijom, status promjena | NOT EXECUTED | NOT EXECUTED | Dozvole za kameru/fotografije i veličina fajla. |
| Glasanje i pokušaj duplog glasa | NOT EXECUTED | NOT EXECUTED | Uskladiti s pravno potvrđenom policy. |
| Background/foreground, restart aplikacije | NOT EXECUTED | NOT EXECUTED | Bez curenja tokena ili tuđih podataka. |
