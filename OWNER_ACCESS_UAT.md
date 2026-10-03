# UAT — multi-ZEV owner pristup

Dodatni granularni UAT: Owner A vidi vlastitu karticu i `ZevMembers`/svoje `PersonOnly` dokumente kada su `Finance.View`/`Documents.View` efektivni. `Deny Documents.View` odbija i direktni list poziv; `Grant BankAccounts.View` Owneru ne otvara bankovne račune jer admin API zahtijeva osoblje. Provjeriti i direktni ID drugog vlasnika. Detalji ključeva su u `GRANULAR_PERMISSIONS.md`.

Testirati na izolovanoj Development/UAT instanci; ne resetovati postojeću bazu. Prije migracije napraviti backup. Koristiti zasebne testne šifre i naloge, bez lozinki u repozitoriju. Za istorijski scenario privremeno uključiti `Privacy__HistoricalSelfAccess=true`; default je `false`.

| Korak | Akcija | Očekivanje |
|---|---|---|
| 1 | System Admin otvori Organizacije, kreira Org A/B i ZEV A/B | vidi oba; nijedan finansijski podatak se ne dijeli preko orga |
| 2 | Kreira Person A, User A i poveže ih; Person A dobije A1 | status veze `Pending`, User A nema ZEV A |
| 3 | U „Uloge i pristupi“ potvrdi vezu uz razlog | status `Verified`, User A bez F5 vidi ZEV A, A1 i samo vlastitu karticu |
| 4 | Kreira Person B/User B, B1 u ZEV B i potvrdi vezu | User A ne vidi B; User B ne vidi A |
| 5 | Kao User A pokuša URL ZEV B, charge/payment B, private dokument B i sirovi file ID | 404; bez PII druge osobe u JSON-u |
| 6 | Objavi `ZevMembers` dokument u A, `PersonOnly` za A, `ManagersOnly` u A | User A vidi prva dva; ne vidi treći; direktan download provjerava isto |
| 7 | Doda suvlasnika Person C/User C na A1 s udjelom 0,5 i verifikacijom | A i C imaju pristup jedinici; nema međusobnog email/telefon/JIB u owner DTO |
| 8 | Zatvori vlasništvo A do današnjeg datuma | User A odmah gubi aktivni ZEV A, nove objave/dokumente/glasanja/kvarove |
| 9 | Sa `HistoricalSelfAccess=false`, traži staru karticu | odbijeno; sa `true` vidi samo vlastitu istorijsku karticu i račune iz perioda vlasništva |
| 10 | Manager A pokuša kreirati override za ZEV B | 404; OrgAdmin A ne vidi Org B |
| 11 | Manager A daje privremeni `Grant zev.read` User X bez vlasništva, uz razlog/do | X vidi osnovni ZEV A, ali ne tuđi dug, račun, uplatu ni privatni dokument |
| 12 | Manager A doda `Deny zev.read` X, potom opozove | deny odmah gasi pristup; opoziv vraća važeći grant; oba događaja su auditirana |
| 13 | Pokuša ručno `Owner` članstvo i override nad System Adminom | odbijeno |
| 14 | Deaktivira Person/User, provjeri refresh i ponovnu verifikaciju | pristup nestaje; reaktivacija sama ne vraća verifikovanu vezu |
| 15 | Provjeri audit za link, override, vidljivost dokumenta i osjetljivi download | akter, tenant, entitet, vrijeme, razlog gdje je obavezan |
| 16 | Kao finansijski administrator izveze godišnji PDF i XLSX otvorenih stavki | oba exporta daju audit red s akterom, ZEV-om, filterima i correlation ID-em |
| 17 | Nakon isteka vlasništva ostavi naslijeđeni ručni `Owner` red i zaseban `Grant zev.read` | korisnik može otvoriti samo eksplicitno zajednički sadržaj, ne Owner-role, PersonOnly ili PropertyUnit dokumente |

API automatizacija pokriva ključne pozitivne/negativne slučajeve u `TenantAccessTests`, `TenantApiTests` i `BillingMigrationTests`. Ručni browser UAT, stvarni SQL rollout i pravna potvrda retencije ostaju zasebni gateovi.
