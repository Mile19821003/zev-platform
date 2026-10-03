# UAT — korisničke lozinke

Izvoditi na Development/UAT kopiji sa testnim nalozima. Ne prikazivati ni zapisivati stvarne lozinke u screenshotove, logove ili tiket. Prije primjene migracije napraviti backup; ne resetovati SQL volume.

| Korak | Akcija | Očekivanje |
|---|---|---|
| 1 | System Admin otvori „Korisnici sistema“, pokuša slabu lozinku, zatim generiše i kopira jaku za novi testni nalog | slaba lozinka je odbijena; generisana je dostupna samo dok je forma otvorena i briše se nakon kreiranja |
| 2 | Za testnog korisnika otvori „Resetuj lozinku“, generiše lozinku, kopira je i uključi „Zahtijevaj promjenu“ | jaka lozinka se vidi samo u otvorenom modalu; potvrda i reset rade |
| 3 | Zatvori modal i ponovo ga otvori | prethodna lozinka nije dostupna |
| 4 | Prijavi testnog korisnika starom lozinkom | odbijeno; stari refresh token i stari access JWT su nevažeći |
| 5 | Prijavi se privremenom lozinkom | otvara se samo ekran „Promijeni lozinku“, bez ZEV/finansijskih podataka |
| 6 | Unesi različite potvrde ili lozinku bez kompleksnosti | jasna poruka; zahtjev za promjenu ostaje |
| 7 | Unesi novu valjanu lozinku | `MustChangePassword=false`, normalna aplikacija je dostupna; privremena lozinka više ne radi |
| 8 | Kroz „Moj profil“ pokušaj promjenu s pogrešnom trenutnom lozinkom, pa s ispravnom | prvi pokušaj odbijen; drugi uspije i opozove prethodne sesije |
| 9 | Kao Manager i OrgAdmin druge organizacije pozovi reset API | 403; ciljna lozinka nije izmijenjena |
| 10 | Provjeri odgovor `/api/me`, listu korisnika i audit | nema lozinke ni hasha; postoje `PASSWORD_RESET_BY_ADMIN`, `PASSWORD_CHANGE_REQUIRED`, `PASSWORD_CHANGED_BY_USER` s akterom, ciljem, vremenom i correlation ID-em |
| 11 | Ponovi reset za deaktiviran testni nalog pa ga aktiviraj | reset je dozvoljen; prijava radi tek poslije aktivacije i, ako je označeno, prisilne promjene |
| 12 | Na mobilnoj aplikaciji se prijavi privremenom lozinkom, promijeni je i ponovo pokreni aplikaciju | vidi ekran prisilne promjene; novi refresh token je u SecureStore i može se rotirati poslije restarta |

Reset tuđeg naloga je trenutno dozvoljen samo System Administratoru. OrgAdmin/Manager reset nije omogućen bez posebne buduće politike. Migracija `20260930150000_PasswordManagement` dodaje `MustChangePassword` i `SecurityVersion` korisniku, verziju refresh sesiji i širi polje audit akcije; postojeći korisnici/sesije imaju verziju 0. Novo izdati access JWT uključuje verziju; stari JWT bez verzije zahtijeva osvježavanje/prijavu. Mobilni `/api/me/password/mobile` nakon promjene vraća novi access i refresh token; web endpoint vraća access token i postavlja HttpOnly refresh cookie.
