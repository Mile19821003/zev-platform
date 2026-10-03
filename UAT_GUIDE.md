# UAT vodič

## Finansijski seed

Iz PowerShella u korijenu repozitorija pokrenuti `./SEED_UAT_FINANCE.ps1`. Skripta koristi postojeći Docker Compose SQL volume i ne poziva `down -v`, brisanje ili konačno knjiženje. API CLI dozvoljava seed samo kada je okruženje `Development` ili `UAT` i kada je eksplicitno proslijeđeno `Seed__UatFinanceEnabled=true`. Skripta ažurira image API-ja, čeka spremnost baze i vraća non-zero status ako bilo koja provjera ne prođe.

Seed koristi TEST/TEST1/TEST2/ZGR-A/UL-A, ali za testne stanove, vlasnike i stavke računa koristi isključivo UAT šifre: UAT-A01, UAT-A02, UAT Test Vlasnik A/B, UAT-OD1, UAT-RF1 i UAT-CIS1. Obični A-01/A-02 i OD1/RF1/CIS1 ostaju netaknuti. Postojeći UAT zapisi se ponovo koriste samo ako odgovaraju definiciji; u suprotnom seed vraća konkretan konflikt. Historijske tarife, vlasništva i finansijski zapisi se ne mijenjaju. Ako za 10/2026 već postoji knjižen obračun, preview se ne može ponoviti i seed se završava greškom bez novog knjiženja. UAT preview bira samo ta dva stana i tri UAT tarife, pa 01.10.2026, dospijeće 30.10.2026 iznosi UAT-A01 = 53,00 KM, UAT-A02 = 69,00 KM, ukupno 122,00 KM čak i kada TEST1 sadrži druge stanove. UAT tarife su aktivne, prikazuju se na računu i označene su kao opcionalne da ne uđu u standardni obračun bez eksplicitnog izbora. Standardni obračun i dalje obuhvata sve aktivne stanove ZEV-a, uključujući UAT stanove; zato ne knjižiti period dok se ne pregleda puni preview. Seed nikada ne knjiži. Rezultat prikazuje PASS/FAIL po grupama i stvarne iznose.

Opcionalni ograničeni manager nastaje samo uz procesnu varijablu `UAT_FINANCE_MANAGER_PASSWORD` (najmanje 12 znakova); `UAT_FINANCE_MANAGER_EMAIL` je opcionalna, sa podrazumijevanim `uat.finance.manager@example.test`. Postojeći korisnik sa tom adresom mora imati odgovarajuću lozinku i bez dodatnih aktivnih pristupa; seed mu ne resetuje lozinku niti oduzima prava. Čuvati lozinku u sigurnom lokalnom secret store-u i ne zapisivati je u historiju terminala ili `.env` pod verzionom kontrolom. Bez lozinke se korisnik preskače, dok ostatak finansijskog seeda ostaje potpuno funkcionalan.

Koristiti sintetičke osobe i odvojeni staging/test ZEV, ne stvarne finansijske podatke. Za svaki scenarij evidentirati osobu/ulogu, datum, build, ulazne podatke, očekivani/stvarni odgovor, screenshot i bug ID. `FINAL_TECHNICAL_CLOSURE.md` je trenutna tehnička matrica; `FINAL_ACCEPTANCE_STATUS.md` je historijski presjek.

| Persona | Pozitivan tok | Negativna provjera |
|---|---|---|
| System Administrator | Organizacija create/edit/deactivate/activate; ZEV create/edit/deactivate/activate; zgrada → ulaz → jedinica → lice → vlasništvo | Neaktivna organizacija/ZEV ne otvara aktivni tok; kontrolisati audit |
| Manager | U dodijeljenom ZEV-u registar create/edit/status, vlasništvo i finansijski izvještaj | Direktni ID drugog ZEV-a vraća 403/404; ne dobija globalni admin meni/API |
| Owner | Svoje nekretnine, historijski saldo, zaduženja/uplate/uplatnica, dokumenti, objave, kvar/fotografija/status | Tuđe finansije, dokument, raw file ID i uplatnica nisu dostupni |
| Representative | Pregled ZEV-a i održavanja | Finansijski i globalni admin endpointi nisu dostupni |

Finansijski test: tarifa po m²/fiksno/član, idempotentan obračun, djelimična uplata, preplata, ručno sravnjenje, as-of saldo prije/poslije promjene vlasnika. Banka: provjeriti CSV/TXT/XML/CAMT uz anonimizirane stvarne uzorke, dupli import, dvosmislen poziv, povrat i odsustvo tihog brisanja alokacija. Povrat je trenutno **tehnički otvoren**, ne knjižiti ga produkcijski.

Dokument: dozvoljeni PDF/JPEG/PNG, pogrešna ekstenzija, prevelika datoteka, direktni download URL, visibility po osobi i cross-tenant. Kalendar: dospjelo/izvršeno/otkazano, idempotentno generisanje termina. Glasanje i opomene testirati samo kao tehničke nacrte dok poslovna/pravna pravila iz `OPEN_BUSINESS_DECISIONS.md` nisu odobrena.

Obavezni izlazni gateovi: `LOCAL_DOCKER_E2E.ps1` bez SKIPPED ključnih koraka, Android/iOS `MOBILE_UAT_CHECKLIST.md`, kompletan NuGet scan, siguran bank return/reversal, širi SQL load/soak test, stvarni bankarski sample, off-host restore i produkcijski security review. Četiri ciljane SQL race regresije već prolaze. Ne proglasiti Production Ready na osnovu samog builda.
