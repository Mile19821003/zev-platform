# Otvorene poslovne odluke

Faza 1.1: Za koje fondove je bankovni račun obavezan prije izdavanja računa? Novi per-fund prekidač je tehnički dostupan, ali postojeći fondovi ostaju na `false` radi kompatibilnosti dok upravnik ne potvrdi politiku. Potrebno je odlučiti i treba li `true` postati podrazumijevana vrijednost za buduće fondove.

Nijedno pitanje niže nije automatski odobreno tehničkim testom. Posebno ID 16 (opomene) i 23 (formalno glasanje) u `FINAL_TECHNICAL_CLOSURE.md` ostaju BLOCKED.

1. Koje pravno područje i valuta/valute se podržavaju u prvoj verziji? Koja su pravila decimalnih mjesta i zaokruživanja na liniji, dokumentu i izvještaju?
2. Da li je ZEV jedna zgrada, više zgrada ili pravno lice koje obuhvata više ulaza? Koji identifikatori (JIB/PIB) su obavezni i jedinstveni?
3. Kako se obračun i dug dijele kod promjene vlasnika unutar mjeseca? Kako se tretira suvlasništvo i ukupni udio?
4. Koje tarife, datum dospijeća i porezni tretman važe za redovni i rezervni fond? Kako se određuje broj prijavljenih članova za period?
5. Kojim redom se raspoređuje uplata (najstariji dug, fond, kamata, glavnica)? Kako se tretiraju preplate i uplate trećih osoba?
6. Koji je tačan format poziva na broj, kontrolna cifra i domaći QR/2D payment standard? Koje banke i formati imaju prioritet?
7. Koje su granice automatskog sravnjenja za nevažeći ili dupli poziv na broj? Šta se dešava pri povratu uplate?
8. Koja jurisdikcija i vremenske stope važe za zateznu kamatu, opomene i pravne rokove?
9. Ko može odobriti vanredni trošak i do kojeg iznosa? Da li su potrebna dva odobrenja?
10. Koja su formalna pravila glasanja, quorum, većina, ponder i identifikacija glasačke jedinice? Ko vidi pojedinačne glasove?
11. Koja je politika zadržavanja dokumenata, audit zapisa i ličnih podataka, te procedura pristupa/brisanja prema lokalnom zakonu?
12. Koje su RPO/RTO vrijednosti, backup lokacija, period čuvanja i ciljni load profil za SLA 99,5%?
13. Kako se korisnik prvi put poziva u ZEV, ko potvrđuje vlasništvo i kako se uklanja pristup bivšem vlasniku?
14. Da li profesionalni upravnik smije imati uvid u agregirane podatke više ZEV-ova i ko odobrava taj scope?
15. Da li nepotpun zbir vlasničkih udjela (manje od 100%) smije biti aktivan tokom unosa i ko potvrđuje kompletan upis?
16. Da li se površina posebnog dijela množi koeficijentom pri obračunu po m² ili se koeficijent koristi samo za glasanje/posebne tarife?
17. Koji ISO 4217 kod valute se inicijalno koristi po bankovnom računu i kako se tretiraju izvodi bez iskazane valute?
18. Koji potvrđeni domaći format PDF uplatnice i eventualnog QR koda treba primijeniti, sa tačnim poljima i provjerama?
19. Da li kamata teče od dana nakon dospijeća, po kojem zaokruživanju i kako djelimične uplate mijenjaju istorijsku glavnicu kroz intervale?
20. Koji su obavezni rokovi, tekst i kanal dostave opomene/opomene pred utuženje za relevantnu jurisdikciju?
21. Ko odobrava knjiženje rashoda i pravni predmet, i da li su potrebni posebni pragovi/odvojene dužnosti?
22. Da li se u glasačko tijelo ubrajaju sve aktivne jedinice, samo upisane jedinice ili samo vlasnici s povezanim korisničkim nalogom?
23. Koji suvlasnik ima pravo dati jedinstveni glas za jedinicu i da li je potrebna punomoć ili saglasnost ostalih?
24. Koji ponder se primjenjuje u formalnom glasanju: površina, koeficijent, vlasnički udio ili druga veličina?
25. Koji je službeni format uplatnice, obavezni tekst/polja, Unicode font i bankarski/platni QR standard? Generički PDF je samo informativan.
26. Da li saldo vlasnika po periodu predstavlja promet u periodu ili stanje na kraju perioda i kako prikazati naknadne raspodjele uplata?
27. Koliki je maksimalni opseg izvještaja i potrebna retencija izvoza; koje filtere i agregacije korisnici moraju imati u prvoj produkcijskoj verziji?
28. Koji mobilni sigurnosni tok važi za refresh tokene, offline pristup, biometriju i pristup dokumentima na uređaju?
29. Smije li se uplata na račun jednog fonda koristiti za zaduženja drugog fonda? Ako da, koji parovi fondova, odobrenja, transferi i knjiženja moraju nastati? Trenutni engine to ne radi automatski.
30. Da li službeni račun smije sakriti pojedinačnu stavku uz prikaz agregata „Ostala zaduženja“, i prikazuje li prethodni dug na službenom dokumentu? Trenutni PDF je samo informativan.
31. Koji prioritet alokacije treba koristiti za stabilnu referencu stana kroz više perioda? Tehnički podrazumijevano je najstarije dospijeće unutar istog fonda; ovo nije proglašeno zakonskim pravilom.
32. Je li puni bankarski povrat knjižen kontra originalne uplate prihvatljiv; kako tretirati djelimični povrat, chargeback naknadu, bankovni datum i istorijski `as-of` pogled? Trenutni puni storno zahtijeva isti račun i isti iznos; djelimični ide na ručni pregled.
33. Koji konačni format/kontrolna cifra poziva na broj i politika promjene prefiksa važe za domaće banke? Generički GUID format nije potvrđeni bankarski standard.
34. Koji dokaz i proces (identifikacija uživo, vlasnički list, punomoć, dvofaktorska potvrda) mora System Admin provjeriti prije `Person.UserLinkStatus=Verified`; smije li OrgAdmin preuzeti ovu radnju i uz kakvo odvajanje dužnosti? Stare veze su migrirane u `Pending` radi sigurnog pregleda.
35. Treba li `Privacy:HistoricalSelfAccess` ostati isključen ili uključiti bivšim vlasnicima ograničen finansijski uvid? Koji datum definira „period vlasništva“ za naknadnu uplatu, storno, ispravku računa i izvoz kartice? Trenutni tehnički filter je datum zaduženja/uplate unutar `[ValidFrom,ValidTo)`.
36. Smije li suvlasnik vidjeti cijeli finansijski zapis jedinice ili samo svoj udio/osobu? Trenutno vidi vlastiti `PersonId` i zajedničku jedinicu, ne tuđe kontakte niti privatne dokumente.
37. Ko odobrava `ZEV_SHARED` objavu pojedinog dokumenta, zajedničke fakture i finansijskog agregata? Finansijski/pravno tipizirani dokument trenutno ne može biti `ZevMembers`; owner agregatni finansijski izvještaj nije objavljen.
38. Koje dozvole Manager smije delegirati, mogu li se tražiti dva odobrenja za `Grant`/`Deny`, koji je maksimalni rok izuzetka i šta se dešava kod konflikta s OrgAdminom? Trenutno se grantor scope i cilj OrgAdmin/SystemAdmin ograničavaju tehnički.
39. Koji su pravna osnova, svrha, retencijski rokovi i legal-hold pravila po kategoriji (finansije, audit, dokumenti, identitet, glasanje, kvarovi)? Nije implementiran automatski purge niti GDPR zaključak.
40. Kada su dozvoljeni pseudonimizacija, anonimizacija i export evidencije subjekta bez narušavanja zakonski čuvane finansijske/audit istorije? Trenutno su dostupne deaktivacija naloga/osobe i revokacija veze, ne kompletan subject-rights workflow.
41. Da li Organization-scope `Accountant` iz stare arhitekture ostaje dozvoljen? Novi Manager je ZEV-scoped, a stari organization-wide Manager se migracijom pretvara u OrgAdmin.
42. Treba li OrgAdmin dobiti pravo reseta lozinki korisnika svoje organizacije i kako utvrditi isključivu pripadnost korisnika kada isti nalog ima više organizacija? Trenutno samo System Administrator može resetovati tuđe lozinke; Manager nema pravo.
43. Koji je službeni format/numeracijski niz izlaznog računa, reset po kalendarskoj godini i ZEV-u, dozvoljeni prefiks te tretman praznina nakon otkazivanja? Trenutno `INV-{godina}-{sekvenca:000000}`, sekvenca po ZEV-u/godini, prefiks konfigurabilan.
44. Treba li prethodni saldo na računu uključiti neraspoređeni kredit osobe, i ako da, kako ga pravno i knjigovodstveno vezati za jednu od više jedinica/fondova? Trenutno snapshot uključuje samo alokacije na prethodne Charge stavke iste jedinice/osobe.
45. Kako prikazati i raspodijeliti popust i centna zaokruživanja kod više suvlasnika, te treba li iskazati udio kao zasebnu kolonu na službenom PDF-u? Trenutno se svaki vlasnički udio zaokružuje po liniji, fiksni popust se dijeli po udjelu.
46. Koja je odobrena forma službenog izlaznog računa: obavezni elementi, porezni tretman, Unicode font, e-račun i javni bankovni podaci? Trenutni PDF je tehnički snapshot dokument, ne pravno certificiran obrazac.
47. Ako jedan ZEV koristi više valuta po fondovima, da li su potrebni odvojeni računi ili konverzija? Trenutni obračun odbija kombinaciju valuta bez konverzije; bez aktivnog računa koristi `Finance:DefaultCurrency` (podrazumijevano `BAM`).
48. Da li je dopušten formalni storno već korigovanog računa, i treba li tada poništiti i sve korektivne dokumente zasebnim lancem? Trenutna tehnička politika to odbija; dozvoljena je nova Credit/Debit korekcija ili storno nekorigovanog `Issued` računa.
49. Treba li nakon storna ili umanjenja automatski prenijeti oslobođeni kredit na drugi dug, kojim redom i preko kojih fondova? Trenutno kredit ostaje neraspoređen i ovlašteni korisnik ga može ručno povezati u istom fondu i za istu osobu.
50. Koji su službeni sadržaj, porezni tretman, statusi odobravanja i izdavanja, rokovi i format storno/knjižnog odobrenja/terećenja? Trenutni `Issued` PDF je tehnički auditabilan, nije pravno certificiran obrazac; refundacija i automatsko izdavanje zamjenskog mjesečnog računa nisu pretpostavljeni.
51. Da li `STO` i `COR` numeracija mora biti zajednička ili odvojena po godini/ZEV-u, šta se dešava s prazninama i koji su konačni prefiksi? Trenutno imaju odvojene godišnje nizove i konfigurabilne različite prefikse.
52. Koja poslovna vremenska zona određuje pravni datum izdavanja korektivnog dokumenta i godišnju numeraciju? Trenutni tehnički tok koristi UTC datum, kao postojeći Invoice V2; lokalni datum može biti drugi oko ponoći.
53. Da li su gotovinske ručne uplate dopuštene, uz koje blagajničke dokaze, limit i odobrenje? `Payments:AllowManualCash` je podrazumijevano `false`; `Migration` i `Other` traže obavezan razlog.
54. Treba li automatsko korištenje kredita uključivati dugove drugih jedinica istog vlasnika i kojim redom pri jednakom dospijeću? Trenutno je dopušteno unutar iste osobe, fonda i valute, po dospijeću/periodu/ID-u; ZEV politika je podrazumijevano isključena.
55. Da li je potreban dvostruki potpis/odobrenje za ručno identifikovanje uplatioca, raspoređivanje i reversal, te treba li razlog biti obavezan za svaku ručnu raspodjelu? Trenutno se koriste granularna prava i EF audit; poseban approval workflow nije definisan.
56. Koji je dozvoljeni postupak refundacije neraspoređenog kredita, djelimičnog bankovnog povrata i prenosa kredita između fondova/valuta? Nije implementiran bez odobrenog poslovnog i računovodstvenog pravila.
57. Koja poslovna vremenska zona određuje datum ručne uplate i `as-of` kartice? Server odbija datum poslije tekućeg UTC datuma; lokalni datum oko ponoći može odstupati.
58. Da li `Payments.ChangePayer` zahtijeva dvostruko odobrenje ili dodatni dokaz i smije li to pravo dobiti Accountant? Tehnički default je samo SystemAdmin/OrganizationAdministrator; Manager i Accountant ga ne dobijaju automatski.
59. Kako tačno pripisati valutu starom `Charge` bez izdatog Invoice snapshot-a kad fond mijenja bankovni račun ili ima račune različitih valuta? Faza 3.3 koristi samo izdati snapshot ili jednoznačnu historijsku alokaciju; inače vraća `UNKNOWN`. Aktivni račun i trenutni default više nisu dokaz historijske valute. Treba odobriti postupak ručnog potvrđivanja stare valute uz audit.
60. Da li historijski `as-of` i puni povrat zahtijevaju tačan intradnevni trenutak i lokalnu vremensku zonu? `Charge`/`Payment`/`PaymentAllocation` koriste UTC vrijeme nastanka, ali `Payment.ReversedOn` je samo datum.
61. Treba li `from` na kartici vlasnika mijenjati lifetime saldo ili samo paginiranu listu događaja? Trenutno je summary stanje do `to`, dok `from` ograničava događaje.
# Finansije V2, Faza 3.2 (01.10.2026)

- Potvrditi kako trajno označiti valutu starih `Charge` redova bez `Invoice` snapshot-a kada se fondovski bankovni račun ili zadana valuta kasnije promijene. Sadašnja read projekcija koristi aktivni račun odnosno `Finance:DefaultCurrency`.
- Definisati politiku i kurs/izvor kursa za kombinovane multi-currency globalne/godišnje izvještaje. Dok nije potvrđeno, kombinovani godišnji izvještaj je blokiran, a globalni ukupni iznosi su `null`.
- Potvrditi valutu starih rashoda koji nemaju trajni currency snapshot. Jednovalutni globalni report pretpostavlja poznatu valutu ZEV-a, što zahtijeva UAT potvrdu.
- Dogovoriti verzionisanje legacy report/portal API klijenata nakon uklanjanja nepagiranih `charges[]`/`payments[]` projekcija; novi klijenti koriste `balances[]` i `events[]`.

# Finansije V2, Faza 3.3 (02.10.2026)

- Odobriti trajni currency snapshot za nove rashode, pravne predmete i opomene. Stari iznosi nemaju pohranjenu valutu; read model ih označava `UNKNOWN`, a nova opomena odbija miješane/nepoznate dospjele stavke. Postojeći historijski pravni dokumenti ostaju bez dokazive valute.
- Definisati da li se legacy `Charge` smije retrospektivno klasificirati po jedinoj historijskoj uplati ili samo ručnom potvrdom, te kako postupiti kada se na istom dugu kasnije pojave različite valute.
- Odobriti FX pravilo ili odvojene viš-valutne godišnje izvještaje; jedinstveni scalar izvještaj se do tada odbija.
