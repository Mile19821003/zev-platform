# Domenski model — izvedeno stanje

Faza 2: `FinancialDocument` (`Storno`, `Credit`, `Debit`, `DocumentStatus=Issued`) ima `OriginalInvoiceId`, `RequestId`, vlastiti broj/seriju, datum, razlog, aktera i snapshot ZEV-a/osobe/jedinice/originalnog broja. `FinancialDocumentLine` veže izvorni `InvoiceLine` kada se koriguje postojeća stavka te novi potpisani `Charge`. `Charge.FinancialDocumentId`, `ReversesChargeId`, `EffectiveOn` i `PaymentAllocation.FinancialDocumentId`, `ReversesAllocationId` čuvaju finansijski i alokacijski reversal bez izmjene iznosa postojećih redova. `Invoice.DocumentStatus` prelazi `Issued → Corrected/Cancelled`; storniran račun ne može biti ponovno storniran, a trenutno se korigovan račun ne stornira nego koriguje novim dokumentom.

Faza 1.1: `InvoiceLine.TariffVersionId` i `Charge.TariffVersionId` su opcionalni za slobodne ručne stavke. Razlog se čuva u nepromjenjivom `Charge.Note` i `InvoiceLine.NoteSnapshot`, uz `AuditLog.AfterJson`. `Fund.RequiresBankAccountForInvoice` je konfigurabilan uslov izdavanja; postojeći fondovi nakon migracije imaju `false` dok ih ovlašten korisnik eksplicitno ne podesi.

## Finansije V2 — trajni izlazni račun

`BillingRun` proizvodi `Invoice` po `(ZevId, Period, PropertyUnitId, PersonId)` i `Charge` po odabranoj stavci. Svaki `InvoiceLine` referencira tačno jedan postojeći `Charge`; `PaymentAllocation` i dalje zatvara `Charge`. `Invoice.DocumentStatus` razlikuje životni ciklus dokumenta od izračunatog `PaymentStatus`. U fazi 1 se izdaje `Issued`, bez aktivnog storna ili korektivnog dokumenta. Invoice i linije su nepromjenjivi; audit bilježi njihovo kreiranje i aktera.

`Invoice.PreviousBalanceSnapshot` = prethodna zaduženja iste jedinice/osobe minus tada aktivne alokacije za prethodne periode; neraspoređeni kredit osobe nije automatski pripisan konkretnoj jedinici. `TotalDueAtIssue` je taj saldo plus novo zaduženje. Trenutno otvoreno računa se zasebno iz `Charge`/aktivnih `PaymentAllocation`; kasnije uplate ne mijenjaju PDF. Procentualni/fiksni popusti, opis, jedinica mjere, količina, cijena, ručni iznos i razlog unose se u postojeći `BillingDraftAdjustment` i snimaju na `InvoiceLine`. Iznosi se zaokružuju na dvije decimale po stavci, postojeći `BillingCalculator`.

Prethodni opis u nastavku dokumenta o nepostojanju trajnog `Invoice` opisuje historijsko stanje prije ove migracije, ne trenutno stanje.

Aktuelni tehnički acceptance i odstupanja od ciljnih workflow-a su u `FINAL_TECHNICAL_CLOSURE.md`.

## Identitet i vlasništvo

`Organization` upravlja više `Zev` zajednica. ZEV ima zgrade, ulaze i posebne dijelove. `Person` može biti fizičko ili pravno lice; `AppUser` je login identitet opcionalno vezan za osobu. `Ownership` je vremenski interval osobe i posebnog dijela, s udjelom i početkom/krajem važenja. Promjena vlasništva ne mijenja historijski `Charge.PersonId`. Preklapanja provjerava transakcijski `OwnershipService`; nepotpun zbir udjela ostaje dozvoljen dok se ne potvrdi poslovno pravilo.

`Membership` dodjeljuje `OrganizationAdministrator` na organizaciji ili `Manager`/`Accountant`/`BuildingRepresentative` na ZEV-u. System Administrator ostaje globalna oznaka na nalogu. `Owner` je izveden, ne ručno članstvo: `Person.UserId`, `UserLinkStatus=Verified` i aktivni `Ownership` daju minimalni pristup. Osoba se može povezati i bez naloga; nova/promijenjena veza je `Pending` do admin verifikacije. `AccessOverride` dodaje vremenski ograničen `Grant`/`Deny` po ZEV-u i permissionu, s razlogom/opozivom; `Deny` pobjeđuje. `Privacy:HistoricalSelfAccess` podrazumijevano isključuje bivše vlasnike, a kad je uključeno daje samo istorijski self-finance. Detalji u `ACCESS_CONTROL_AND_PRIVACY.md`.

## Finansije

**Aktuelni Payments V2 model (01.10.2026):** `Invoice` i `InvoiceLine` su trajni snapshot izdatog računa; `Charge` je izvor potraživanja, uključujući legacy Charge bez Invoice. `Payment.SourceType` je `BankTransaction` ili `Manual`; manualni izvor zahtijeva razlog, metodu i request ID, a bankovni FK. `PaymentAllocation.OperationId` identifikuje raspodjelu; negativni red sa `ReversesAllocationId` oslobađa kredit. Kredit nije saldo kolona nego `Payment.Amount − neto alokacije`. `Zev.AutoApplyPaymentCredit` je politika s defaultom `false`. Historijski opis niže da Invoice tabela ne postoji više ne važi za Finansije V2.

`TariffVersion` ima vremenske intervale; `Charge` čuva verziju, količinu, stopu, udio i iznos. Nema zasebnog `ChargeLine`: jedan red predstavlja jednu tarifu/jedinicu/odgovornu osobu. `BillingRun` ima jedinstven ključ ZEV/period za idempotentnost. `Payment` i `PaymentAllocation` su odvojeni; nealokirani iznos ostaje kredit. `FundTransaction` je izvor stanja fonda. `Expense` sadrži podatke ulaznog računa; nema zasebne `VendorInvoice` tabele. Storno rashoda je protivknjiženje sa referencom na original.

## Banka i sravnjenje

`BankStatementImport` ima checksum, parser i metapodatke fajla. `BankTransaction` ima redni broj unutar izvoda i poziv na broj, ali ne i nezavisni bank ID/fingerprint. `ReconciliationAttempt` veže transakciju, uplatu, pravilo i rezultat; nema confidence metrike jer je podudaranje determinističko. Nejasno se označi `ManualReview` i može se ručno rasporediti.

## Operativni moduli

`MaintenanceTicket` i `WorkOrder` imaju historiju statusa. `MaintenancePlan` proizvodi planirane obaveze. `Announcement` cilja ZEV/zgradu/ulaz ili ulogu. `Ballot` snima konfiguraciju kvoruma/većine, `BallotEligibility` zamrzava biračko tijelo, a `Vote` je auditabilan i zaključiv. `ManagedDocument` pokazuje na `StoredFile` i nosi `ManagersOnly` (ADMIN_ONLY), `ZevMembers` (ZEV_SHARED), `PersonOnly`, `PropertyUnit` ili `Role` vidljivost. Privatni dokument traži dodatnu provjeru verifikovanog ownera/scope-a i na download ruti. `LegalCase` veže osobu, ZEV, dokumente i iznose, bez hardkodiranih stopa ili rokova.

## Invarijante

- Svaki poslovni red ima konzistentan `OrganizationId` i `ZevId` kada pripada ZEV-u; FK i servisne provjere sprečavaju cross-tenant veze.
- Finansijske vrijednosti se čuvaju kao `decimal`, sa centralnom politikom preciznosti i zaokruživanja.
- Knjižene stavke i audit se ne brišu. Duplikati obračuna, importa i glasova imaju database unique constraints.
- Većina lista je paginirana; neki kalendarski i vlastiti pregledi imaju samo gornju granicu. XLSX izvoz otvorenih stavki ograničen je na 10.000 redova. PDF izlaz je informativan i zahtijeva produkcijsku validaciju.

As-of izvještaji otvorenih stavki, dužnika i kartica uzimaju alokacije čija `Payment.PaidOn` nije nakon izabranog datuma. Aging prikazuje `NotDue`, 1–30, 31–60, 61–90, 91–180, 181–365 i >365 dana. Od 28.09.2026. postoji tehnički puni bankovni povrat: `Payment.Status=Reversed`, `Payment.ReversedOn` čuva datum, originalna `PaymentAllocation` ostaje neizmijenjena, negativni `FundTransaction` referencira original, a `ReconciliationAttempt` veže povratnu bankovnu transakciju i originalnu uplatu s razlogom. Aktuelni saldo izuzima alokacije stornirane uplate; as-of ih uzima do dana prije povrata. SQL/UAT validacija ovog pravila i djelimični povrati nisu zatvoreni.

`TariffVersion` sada nosi `Description`, `IsOptional`, `ShowOnInvoice`, `DisplayOrder`; `Charge` nosi `Note` i `IsManualOverride`. Jedan `Charge` je jedna stvarna linija računa za jedinicu/osobu/period, bez nove `ChargeLine` ili `Invoice` tabele. `InvoiceView` je read-only projekcija, uključujući prethodni i tekući saldo; za istu jedinicu različiti historijski vlasnici imaju odvojene redove. `PaymentReferenceProvider` daje account referencu ZEV/jedinica/osoba; stari pojedinačni `Charge.Reference` ostaje validan. `ONE_TIME` važi samo u mjesecu početka, a `MANUAL_AMOUNT` zahtijeva konkretan draft iznos.
