# Status Faze 1

## Implementirano

- ASP.NET Core 10 API i EF Core 10 SQL Server model s početnom migracijom.
- Korisnici s provjerenim password hashingom, bootstrap sistemskog administratora, JWT access token (15 minuta) i hashirani rotirajući refresh token u HttpOnly cookie-ju (30 dana).
- Organizacija i ZEV, članstva po scope-u, katalog permissiona i role-permission veza. Globalni admin ima odvojenu DB provjeru; tenant pristup se validira na serveru.
- Audit promjena korisnika, članstava, permissiona, organizacija, ZEV-ova i refresh sesija, bez hash lozinke ili refresh tokena u snapshot-u.
- API rute za prijavu/odjavu, profil, paginiranu listu dostupnih ZEV-ova, čitanje organizacije/ZEV-a i osnovni administratorski onboarding.
- React/TypeScript web prijava i osnovni layout, OpenAPI, rate limiting, CORS, HTTPS/proxy konfiguracija, liveness/readiness endpoint i razvojni Compose.

## Provjereno

- `dotnet test`: 5/5 testova prolazi, uključujući API cross-tenant i RBAC provjeru.
- `npm run build`: TypeScript i Vite build prolaze.
- `npm install`/audit: 0 prijavljenih ranjivosti za zaključane web zavisnosti.
- `dotnet ef migrations has-pending-model-changes`: nema razlika između modela i migracije; idempotentni SQL skript se generiše.

## Preostali gateovi i rizici

- SQL Server migracija nije izvršena na testnoj bazi jer Docker/SQL Server nisu dostupni u ovom okruženju. Prije primjene izvan razvoja pregledati migraciju, pokrenuti `dotnet ef database update` na izolovanoj testnoj bazi i provjeriti rollback/restore.
- Potrebni su SQL Server integration testovi za refresh rotaciju pod konkurencijom, jedinstvene indekse, FK i audit transakciju. InMemory testovi te garancije ne dokazuju.
- Inicijalno kreiranje korisnika koristi administratorski zadanu lozinku. Siguran poziv korisnika, promjena/reset lozinke, upravljanje životnim ciklusom članstva i MFA su naredni sigurnosni zadaci prije produkcije.
- File-storage health, backup/restore automatizacija, 99,5% operativni SLA i poslovni/performance testovi dolaze u narednim fazama.
- Otvorena poslovna pravila su navedena u `OPEN_BUSINESS_DECISIONS.md`; finansijske i pravne funkcije se ne implementiraju dok nisu potvrđena.
