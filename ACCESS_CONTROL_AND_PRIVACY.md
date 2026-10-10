# Multi-ZEV pristup i privatnost — izvedeno stanje (30.09.2026.)

Granularni nastavak: `GRANULAR_PERMISSIONS.md` definiše matricu User+ZEV, role default, `Grant`/`Deny` prioritet i server-side akcijske provjere. `Otvori ZEV` sada postavlja aktivni UX kontekst i ulazi u pregled izabranog ZEV-a; promjena u zaglavlju resetuje aktivnu stranicu, a `/api/me/zevs` ponovo izlaže efektivni katalog pri access refreshu. Ovo ne mijenja tenant/resource filtere opisane ispod.

## Sigurnosna granica

`Organization` grupiše ZEV-ove, ali finansijski i drugi poslovni redovi se i dalje autorizuju po `ZevId`. Backend, ne aktivni izbor u web zaglavlju, odlučuje o pristupu. Status odbijenog zahtjeva određuje pravilo „403 ili 404“ (ispod). JWT sadrži identitet; role/scope se pri zahtjevu čitaju iz baze.

`EffectiveAccessService.GetEffectiveZevAccess(userId, zevId)` kombinuje aktivnog korisnika/ZEV/organizaciju, sistemsku ulogu, organizacijsko ili ZEV članstvo, `RolePermission` template, verifikovano aktivno vlasništvo i važeće ručne izuzetke. Vraća `CanRead`, `CanManageFinances`, `CanManageLegalCases`, `CanManageMaintenance`, `IsOwner` i izvore `SYSTEM_ROLE`, `ORG_ROLE`, `ZEV_MEMBERSHIP`, `OWNERSHIP`, `MANUAL_OVERRIDE`. `TenantAccess` koristi ovaj servis za guardove. Eksplicitni `Deny` nad pravom ima prednost nad grantovima; `Deny zev.read` gasi i upravljačka prava. System Administrator je izuzet od override-a, ali njegove promjene ostaju auditirane.

| Uloga | Scope | Pristup |
|---|---|---|
| `IsSystemAdministrator` | sistem | sve aktivne organizacije/ZEV-ovi bez članstva po ZEV-u |
| `OrganizationAdministrator` (`ORG_ADMIN`) | `OrganizationId`, `ZevId = null` | svi sadašnji i budući aktivni ZEV-ovi te organizacije |
| `Manager` | `ZevId` | dodijeljeni ZEV, registar, finansije, pravni modul, održavanje |
| `Accountant` | ZEV; naslijeđeni null-scope ostaje moguć | finansije dodijeljenog scope-a |
| `BuildingRepresentative` | `ZevId` | čitanje i održavanje, bez privatnih finansija |
| `Owner` | izveden iz vlasništva | samo vlastiti resursi i eksplicitno zajednički sadržaj |

Nova ručna `Owner` članstva nisu dozvoljena. Stari redovi ostaju u bazi radi istorije, ali **ne** daju owner access ni pristup objavama/dokumentima ciljanim na ulogu `Owner`; i oni traže aktivno verifikovano vlasništvo. Migracija `20260930120000_EffectiveAccessPrivacy` prevodi postojeća organizacijska `Manager` članstva (`ZevId = null`) u `OrganizationAdministrator`, bez brisanja korisnika/vlasništva/finansija. Nova `Manager` članstva moraju imati `ZevId`. Postojeći organizacijski `Accountant` scope ostaje naslijeđeni izuzetak; konačna politika je otvorena odluka.

## 403 ili 404 (od 10.10.2026.)

Za rute unutar ZEV-a (`/api/zevs/{zevId}/...`) backend koristi `TenantAccess.RequirePermissionAsync` /
`RequireAllPermissionsAsync`:

| Situacija | Status |
|---|---|
| Korisnik ne može čitati ZEV (tuđi tenant, nepostojeći ili neaktivan ZEV, bez verifikovanog vlasništva/članstva) | **404** — postojanje ZEV-a i njegovih resursa se ne otkriva |
| Korisnik može čitati ZEV, ali nema traženo pravo (npr. `Payments.Reverse`, `BankAccounts.Create`, `Reports.View` za vlasnika, `Expenses.Reverse` za računovođu, odbijen `DENY`) | **403** |
| Delegiranje grubog prava koje dodjeljivač nema (`/access/overrides`) ili dodjela detaljnog prava koje nema (matrica) | **403** |
| Resurs ne postoji u tom ZEV-u (pogrešan ID) | **404** |
| Resurs postoji, ali pripada drugoj osobi ili nije vidljiv pozivaocu (`PRIVATE` klasa: kartica/račun/uplatnica tuđe osobe, `PersonOnly`/`PropertyUnit` dokument, fajl, tuđa prijava kvara, jedinica za koju korisnik ne smije prijaviti kvar) | **404** — kao i ranije, da se ne otkriva tuđi podatak |
| Zaštićena meta upravljanja pravima (vlastita prava, organizacijski administrator, sistemski administrator) | **404** |

Provjera prava se radi prije provjere postojanja resursa, pa korisnik bez prava dobija 403 i za nepostojeći ID.
Rute nad organizacijom (npr. hodogram, `/api/organizations/{id}/...`) i vlasničke rute `/api/me/...` nisu dio ovog
pravila i zadržavaju dosadašnje ponašanje (404).

## Verifikovana veza i lifecycle

`AppUser` je login identitet, `Person` poslovni identitet. `Person.UserId` može biti null; status veze je `Pending`, `Verified`, `Rejected` ili `Revoked`. Novo ili promijenjeno povezivanje je `Pending`; migracija sve postojeće veze označava `Pending` radi sigurnog ponovnog pregleda. Sam korisnik ne može potvrditi vezu. System Administrator koristi `POST /api/system/people/{personId}/user-link/status` uz status i razlog. Audit čuva aktera, identitet, prethodni/novi status, razlog i vrijeme. Deaktivacija osobe opoziva vezu; reaktivacija je ne verifikuje automatski.

Samo `Verified` + aktivna osoba + važeće `Ownership` `[ValidFrom, ValidTo)` + aktivna jedinica daje automatski `Owner` pristup ZEV-u. Udjeli suvlasnika se ne miješaju s njihovim privatnim kontaktima: oba verifikovana suvlasnika dobijaju pristup jedinici, ne tuđem emailu/telefonu. Prestanak vlasništva automatski uklanja aktivni owner access bez članstva i bez noćnog joba.

`Privacy:HistoricalSelfAccess` je podrazumijevano `false` u `appsettings.json`; može se postaviti kroz `Privacy__HistoricalSelfAccess=true` tek nakon poslovne odluke. Kada je uključen, bivši verifikovani vlasnik u „Moje zajednice“ dobija samo istorijski finansijski ulaz, bez `zev.read`: kartica filtrira zaduženja/uplate na datume vlasništva, a pojedinačni račun/PDF traži period vlasništva. Ne otvara novi ZEV sadržaj, dokumente, obavještenja, kvarove ni glasanja. Aktivni `Deny zev.read` blokira i istorijski self-access. Periodični/as-of tretman naknadnih uplata zahtijeva poslovnu potvrdu.

## Ručni izuzeci

`AccessOverride` čuva korisnika, ZEV, permission (`zev.read`, `finance.manage`, `legal.manage`, `maintenance.manage`), `Grant`/`Deny`, od/do, razlog, aktera/vrijeme i opoziv. Rute su `GET/POST /api/zevs/{zevId}/access/overrides` i `POST /api/zevs/{zevId}/access/overrides/{id}/revoke`. Grantor mora imati isto pravo u tom ZEV-u i pravo upravljanja; Manager A ne može dodijeliti B. Ne-sistemski akter ne može mijenjati prava organizacijskog administratora; System Administrator ne može biti meta override-a. Ručni `Grant zev.read` korisniku bez aktivnog vlasništva zahtijeva razlog i datum isteka. Takav grant ne daje privatne owner finansije, jer one i dalje traže verifikovanu osobu/vlasništvo. `Deny` ima prednost i kada postoji članstvo ili vlasništvo. Audit loguje kreiranje/opoziv i razlog u snapshot-u.

## Vidljivost resursa

| Klasa | Implementacija |
|---|---|
| `PRIVATE` | vlastita kartica, računi, uplate, uplatnice po `PersonId`/ownership periodu; dokument `PersonOnly` ili `PropertyUnit`; administratori prema modulu |
| `ZEV_SHARED` | objava za ZEV/zgradu/ulaz/ulogu i dokument `ZevMembers`, samo aktivni ovlašteni čitaoci ZEV-a |
| `ADMIN_ONLY` | dokument `ManagersOnly`, dugovanja drugih osoba, pravni/finansijski upravljački API, audit i sravnjenje |

Dokument dodatno podržava `Role` target. Fajl dokumenta se može preuzeti samo putem `community/documents/{id}/download`, koji ponovo provjerava vidljivost i `ZevId`; sirovi `/files/{id}` odbija `ManagedDocument`. Promjena vidljivosti je poseban admin PATCH s razlogom i auditom. Osjetljivo preuzimanje (`!= ZevMembers`) stvara audit red. Finansijski PDF/XLSX export takođe upisuje audit s akterom, ZEV-om, filterima i correlation ID-em. Finansijski/pravni dokument ne može se objaviti kao `ZevMembers` kroz ove rute. Owner DTO za vlastite jedinice i finansije ne vraća listu tuđih osoba, kontakte, JIB ni privatne bilješke. Zajednički finansijski agregati još nemaju workflow odobrenog objavljivanja; nije otvoren owneru admin izvještaj ili lista dužnika.

## Operativne granice

Web osvježava dostupne ZEV-ove/menije nakon promjene vlasništva, veze, članstva, korisničkog statusa i override-a kroz postojeći `dataSync`; izbor aktivnog ZEV-a je samo UX. Ekran pristupa ima pretragu osobe i naloga da se veza/izuzetak može pronaći i izvan prvih 100 zapisa. Regresije potvrđuju da owner API ne vraća tuđi email/telefon/JIB, da se `PersonOnly`/`PropertyUnit`/`Role` dokumenti filtriraju i da stari `Owner` membership ne održava privatni pristup poslije isteka vlasništva. Migracija je inkrementalna, ali forward-only: automatski `Down` je namjerno blokiran jer ne može sigurno razlikovati prevedene stare Manager redove od kasnije kreiranih OrgAdmin redova, niti vratiti izgubljene statusne promjene. **Nije primijenjena na postojeću lokalnu SQL bazu u ovom prolazu.** `BillingMigrationTests` provjerava model/snapshot i generiše SQL bez konekcije; realni SQL rollout traži backup, kontrolisanu primjenu i UAT. Nema automatske anonimizacije, retencijskih rokova, subject exporta niti potvrđene pravne osnove. Tehničke kontrole nisu izjava o GDPR usklađenosti.
