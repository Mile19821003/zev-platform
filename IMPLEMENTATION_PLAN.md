# Plan implementacije

Finansije V2 faza 2: implementirani su storno, Credit/Debit korekcije, povezani dokumenti, timeline, zasebni PDF-ovi, append-only finansijske/alokacijske protivstavke, granularna prava i aditivna migracija `20260930200000_InvoiceLifecycle`. Lokalni build/test status i neizvršeni SQL gate su u `FINANCE_V2_PHASE2_STATUS.md`. Sljedeći korak nije automatsko produkcijsko puštanje, nego SQL integracijski/concurrency test, backup, migracijska revizija i UAT računovodstvena potvrda.

Finansije V2 faza 1 (30.09.2026): trajni `Invoice`/`InvoiceLine`, snapshot PDF, numeracija, popust i legacy read-mode su implementirani u `20260930180000_InvoiceV2`; lokalni SQL apply i produkcijska verifikacija nisu izvršeni u ovom sandboxu. Historijski status je u `FINANCE_V2_PHASE1_STATUS.md`; za aktuelnu fazu 2 pogledati `FINANCE_V2_PHASE2_STATUS.md`.

Faze 1–9 su historijski plan/izvedba. Aktivni tehnički backlog i trenutni acceptance su u `FINAL_TECHNICAL_CLOSURE.md`; ne tumačiti redove niže kao završeno produkcijsko odobrenje.

Svaka faza završava buildom, migracijom na izolovanoj testnoj bazi, testovima, lint/typecheck provjerom i izvještajem o poznatim rizicima. Produkciona baza i stvarni lični podaci se ne koriste.

| Faza | Isporuka | Ključni test/gate |
|---|---|---|
| 1 | Identitet, access/refresh token, organizacijski i ZEV RBAC, audit, SQL model, web layout, health | Login/refresh, cross-tenant zabrana, migracija i build |
| 2 | ZEV, zgrade, ulazi, jedinice, osobe, ownership history | Intervali i udjeli, promjena vlasnika, parent/child tenant FK |
| 3 | Tarife/verzije, fondovi, obračun, kartice vlasnika | Decimal rounding, idempotentnost, transakcija, 1.000+ stavki |
| 4 | Bank parseri, import, uplate, sravnjenje, uplatnice, rashodi | Dupli import, 95% ispravnih poziva na broj, djelimične uplate |
| 5 | Dugovanja, opomene, legal case, izvještaji | Historijski saldo, reversal, PDF/XLSX izvoz |
| 6 | Ticket, work order, maintenance | Status workflow, tenant scope, raspoređivanje |
| 7 | Objave, dokumenti, ankete/glasanje | Permission model, zaključani glasovi, audit |
| 8 | Portal vlasnika, React Native aplikacija | Samo vlastiti podaci, API paritet, upload/download |
| 9 | Performanse, hardening, backup/restore, UAT | 1.000 obračuna <30 s, API <1,5 s u definisanom load profilu |

## Status 27.09.2026.

Faze 2–7 imaju migracije primijenjene na lokalni SQL Server, backend testove i web build. Faza 8 dodaje izvještajni/vlasnički API, web portal i mobilnu osnovu bez promjene sheme. Faza 9 dodaje SQL performance test, sigurnosne provjere i operativne upute. Finalni acceptance audit ima 47/47 backend testova sa oba SQL opt-in testa zaista aktivirana, web build i mobilni typecheck. SQL test generiše 1001 zaduženje na izolovanoj bazi, a backup drill obnavlja odvojenu bazu. To nije produkcijski acceptance: novi Docker end-to-end, pravno/bankarska validacija, kompletni mobilni tokovi i dio izvještaja ostaju otvoreni. `FINAL_ACCEPTANCE_STATUS.md` je važeća matrica.
