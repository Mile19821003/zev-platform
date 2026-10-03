# Lokalni razvoj

Ovo je postojeći projekat. Ne brisati SQL/file Docker volumene. Potrebni su Docker Desktop, Node.js/npm i .NET 10 SDK. Pokretati naredbe iz korijena projekta.

1. `Copy-Item .env.example .env` ako `.env` ne postoji. Postaviti `SA_PASSWORD`, nasumični `JWT_SIGNING_KEY` (najmanje 32 UTF-8 bajta), `BOOTSTRAP_ADMIN_EMAIL` i `BOOTSTRAP_ADMIN_PASSWORD` (najmanje 12 znakova, veliko/malo slovo, broj i specijalni znak za novi bootstrap). Ne commitati `.env`.
2. `docker compose config --quiet`
3. `docker compose build`
4. `docker compose up -d`
5. `docker compose ps`
6. `./scripts/health-smoke.ps1 -BaseUrl http://localhost:8080`

Web je na `http://localhost:5173`, API na `http://localhost:8080`. Compose sadrži `db`, `api` i `web`; SQL i datoteke imaju zasebne volumene. Development API primjenjuje EF migracije na startu. Po bootstrapu administratora ukloniti bootstrap lozinku iz `.env` i ponovo pokrenuti API.

Za puniji lokalni smoke pokrenuti `./LOCAL_DOCKER_E2E.ps1` sa `ZEV_E2E_EMAIL` i `ZEV_E2E_PASSWORD` kao procesnim environment varijablama; za negativni tenant test koristiti ograničen nalog i `ZEV_E2E_FORBIDDEN_ZEV_ID`. Skripta ne briše volume niti štampa token/lozinku. SKIPPED korak nije PASS.

Build/test: `dotnet build src/Zev.Api/Zev.Api.csproj`, `dotnet test tests/Zev.Api.Tests/Zev.Api.Tests.csproj`, `npm ci --prefix web`, `npm run build --prefix web`, `npm ci --prefix mobile`, `npm run typecheck --prefix mobile`. SQL opt-in testovi traže `ZEV_TEST_SQL_CONNECTION` i `ZEV_BACKUP_DRILL_CONNECTION`; koristiti samo testni SQL, jer testovi kreiraju i uklanjaju vlastite izolovane baze. Bez varijabli xUnit prikazuje raniji izlaz kao passed, ne kao executed SQL.

Za mobilni uređaj postaviti `EXPO_PUBLIC_API_URL` na uređaju dostupnu HTTPS adresu, zatim `npm start --prefix mobile`. Typecheck nije zamjena za uređajski UAT.
