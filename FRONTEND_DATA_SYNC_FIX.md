# Frontend data-sync / refresh

## Dijagnoza prije izmjena

- `web/src/api.ts` i `web/src/App.tsx` imaju dva odvojena API klijenta. Oba tretiraju 204 kao uspjeh, ali za svaki drugi uspješan status bezuslovno pozivaju `response.json()`. Uspješan 200/202 bez JSON tijela zato postaje izuzetak i UI može prikazati lažnu grešku.
- Nema query/cache sloja niti centralne invalidacije. Stranice uglavnom lokalno ponovo učitavaju samo svoju listu. `App` učitava profil i prvih 20 ZEV-ova samo pri promjeni tokena; kreiranje ZEV-a ne osvježava taj kontekst. Lookup se učitava samo pri promjeni URL-a/pretrage/tokena, ne pri promjeni povezane kolekcije.
- `OrganizationsPage` obuhvata uspješan mutation i naredni GET jednim `try/catch`: neuspješan GET može prijaviti da snimanje nije uspjelo. `ResourcePage` ima drugačiji obrazac i guta grešku GET-a. Stoga CRUD ponašanje nije ujednačeno.
- Kod membership forme promjena organizacije ne poništava već izabrani ZEV. Lookup ima zaštitu od starog odgovora pri promjeni URL-a, ali čuva prethodnu izabranu opciju i nema invalidaciju nakon mutation-a.
- `setBusy` onemogućava dugme nakon rendera, ali bez sinhrone zaštite dvije brze predaje mogu poslati dupli POST. Paginacija ne vraća korisnika na postojeću stranicu nakon smanjenja ukupnog broja zapisa.

## Centralni fix

- Jedan API klijent (`web/src/apiClient.ts`, izvezen preko `web/src/api.ts`) koristi i `App` i sve admin/portal stranice. JSON se parsira samo kada odgovor ima JSON content type i tijelo. 200/201 JSON, 202, 204 i prazan uspješan odgovor ne prikazuju lažnu mutation grešku. ProblemDetails `detail`/`errors`/`title` se prikazuju kada postoje; fallback razlikuje 400, 401, 403, 404, 409, 422, 429, 5xx i mrežnu grešku.
- Nakon **uspješnog** non-GET zahtjeva klijent objavljuje promjenu. Nema query cache-a: `dataSync.ts` vodi pretplate po resursu (`organizations`, `zevs`, `buildings`, `entrances`, `propertyUnits`, `people`, `ownerships`, `users`, `memberships`) i ZEV-u (`zev:<id>:finance/payments/expenses/legal/maintenance/community/ballots/reports/owner`). Izmjene finansija, uplata i rashoda osvježavaju i relevantne izvještaje, fondove i vlasničku karticu. Izmjena u drugom tabu/postupku osvježava trenutno vidljive pretplate pri fokusu/ponovnoj vidljivosti prozora. Nema full-page reload-a.
- `Lookup` ponovo dohvaća opcije na signal izmjene, koristi `AbortController` pri promjeni parent putanje/pretrage, zanemaruje zakasnjele odgovore i prikazuje loading/error. Admin forme za zgradu, ulaz i posebni dio sada imaju niz Organizacija → ZEV → Zgrada → Ulaz; promjena parenta rekurzivno poništava potomke. UI-only parent polja se ne šalju backendu. Membership ZEV zavisi od izabrane organizacije.
- `ResourcePage` i `OrganizationsPage` osvježavaju listu nakon signala uspješnog upisa, odvajaju grešku upisa od greške GET-a, štite predaju sinhronim `ref` guardom i vraćaju paginaciju na postojeću stranicu kada rezultat postane prazan. Ostali montirani moduli se pretplaćuju na relevantne ključeve i koriste request identity kako stari GET ne bi pregazio novi filter/period/ZEV ili rezultat upisa. Lokalni GET nije dio mutation `try/catch` uspjeha. Centralni klijent spaja identične istovremene upise, a višekoračni import/upload/posting dodatno imaju sinhronu UI zaštitu.
- `App` ponovo učitava `/api/me` i sve stranice `/api/me/zevs` nakon promjene pristupa/ZEV-a i pri fokusu. Na 401 koristi postojeći refresh-token endpoint; za druge greške ne proglašava automatski istek sesije. Role navigacija se prilagođava promjenama pristupa.

## Izmijenjene oblasti

11 funkcionalnih oblasti: auth/dashboard, admin registri i lookup, vlasništvo, finansije, izvodi/uplate, rashodi/dugovanja, pravni modul, održavanje, zajednica/glasanje, izvještaji i vlasnički portal. Centralni fajlovi: `web/src/apiClient.ts`, `web/src/dataSync.ts`, `web/src/formDependencies.ts`, `web/src/useChangeRevision.ts`, `web/src/api.ts`, `web/src/App.tsx`. Povezane stranice su u `web/src/admin/`; regresije su u `web/tests/dataSync.test.mjs` i `web/package.json`.

## Verifikacija

- `npm ci --prefix web` uz `npm_config_cache` usmjeren na lokalni workspace cache: PASS, 0 npm audit ranjivosti. Bez toga pod sandboxom sistemski npm cache vraća EPERM; prvi offline pokušaj s praznim cacheom nije imao pakete.
- `npm run build --prefix web`: PASS (TypeScript + Vite); `npm run typecheck --prefix web`: PASS.
- `npm test --prefix web`: 15/15 PASS. Testovi koriste fake HTTP odgovor i centralne pretplate za 10 traženih create/edit/deactivate/204/error/double-submit scenarija, ZEV-scoped finance invalidaciju, parent reset, focus, klasifikaciju grešaka i izolaciju greške pretplatnika. Testovi traže Node 24+ zbog ugrađenog TypeScript stripovanja. Ovo nisu browser E2E ili testovi protiv stvarnog SQL-a.
- Lokalni API `/health/live` na portu 8080 i web na portu 5173 bili su dostupni. Browser se otvorio na login ekranu. Ručni acceptance tok **TEST REFRESH ORG → ZEV → BUILDING → ENTRANCE → PROPERTY UNIT bez F5 nije izvršen**, jer nije bilo autentifikovane sesije/testnih kredencijala u ovoj provjeri. U postojeću bazu nisu upisivani testni zapisi niti je resetovana.

## Otvorena provjera i ograničenja

- Ručno potvrditi puni lanac, edit/status, membership i finansijske tokove na autorizovanoj lokalnoj/staging sesiji. Zbog izostanka tog browser UAT-a ne tvrdimo da nijedna stranica više ne zahtijeva F5 i ne označavamo globalni bug zatvorenim u `FINAL_TECHNICAL_CLOSURE.md`.
- Lookup učitava do 100 opcija po pretrazi; veći skup se pronalazi upisom termina. Nema WebSocket/server-push osvježavanja dok je tab konstantno aktivan nakon izmjene u drugoj sesiji; fokus/vidljivost ponovo dohvaća aktivne prikaze.
