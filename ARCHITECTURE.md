# Arhitektura i analiza stanja — izvedeno stanje

Finansije V2, faza 2: `InvoiceLifecycleService` knjiži `FinancialDocument` (Storno/Credit/Debit) i njegove nepromjenjive linije u SQL `Serializable` transakciji. `Invoice`/`InvoiceLine` i izdani PDF ostaju snapshot; jedina dozvoljena promjena originala je `DocumentStatus` u istoj transakciji s novim dokumentom. Svaka korektivna linija proizvodi potpisani `Charge`; negativna linija referencira izvorni `Charge`. Ako je izvorna stavka plaćena preko nove obaveze, negativni `PaymentAllocation` referencira izvornu alokaciju i oslobađa kredit bez brisanja uplate ili bankovne transakcije. `PaymentReconciliationEngine` koristi efektivni kapacitet izvornog zaduženja nakon korekcija. Numeracija `STO`/`COR` se zaključava po ZEV-u uz unique indekse. Detalji u `FINANCE_V2_PHASE2_STATUS.md`.

Faza 1.1: Slobodna stavka živi samo u `BillingDraftRequest.ManualLines` do knjiženja. Postojeći transakcijski tok tada stvara `Charge` (`IsManualOverride`, `TariffVersionId = null`) i trajni `InvoiceLine` sa snapshotom i razlogom u napomeni/auditu. `InvoiceNumberProvider` formatira broj bez promjene SQL serijalizacije i jedinstvenih indeksa. Fond može zahtijevati aktivan račun kroz `RequiresBankAccountForInvoice`; preflight i posting to provjeravaju. PDF koristi ugrađeni Noto Sans (OFL), bez server-font instalacije.

## Finansije V2, faza 1 — 30.09.2026.

Konačno mjesečno knjiženje u jednoj `Serializable` transakciji stvara `BillingRun`, `Charge`, trajni `Invoice` i njegove `InvoiceLine` zapise. Zadržani su jedinstveni `(ZevId, Period)` za obračun i `Charge` kao jedina otvorena finansijska stavka; `PaymentAllocation` i sravnjenje i dalje ciljaju `Charge`, ne zasebni saldo računa. `InvoiceLine.ChargeId` je jedinstven. Račun je grupisan po ZEV-u, periodu, jedinici i osobi; suvlasnici dobijaju odvojene račune prema udjelu.

Novi račun trajno čuva broj, datume, raniji saldo pri izdavanju, iznose, primaoca, jedinicu, ZEV, referencu, stavke i račune fondova. PDF izdanog računa čita samo te snapshot vrijednosti, dok API posebno računa trenutno plaćeno/otvoreno iz aktivnih alokacija. `Invoice`/`InvoiceLine` su append-only na EF sloju. `InvoiceNumber` je sekvenca po `(ZevId, NumberYear)`, pod SQL `UPDLOCK`/`HOLDLOCK` nad ZEV-om, zaštićena jedinstvenim indeksima; prefiks određuje `Finance:InvoiceNumberPrefix` (podrazumijevano `INV`).

Prethodni `BillingRun`/`Charge` bez `Invoice` ostaju dostupni putem izričito označenog legacy read-modea; za njih ne stvaramo izmišljene historijske snapshotove. Njihov informativni PDF i raniji saldo ostaju dinamički. `Invoice.Generate` i `Billing.Reverse` su rezervisani permission ključevi bez aktivnog zasebnog endpointa u ovoj fazi; izdavanje zahtijeva `Billing.Post`.

Pri ovom nastavku rada već je postojala funkcionalna Faza 1. Nad istim ASP.NET Core/EF/SQL Server projektom su dodati registri, finansije, bankarstvo, rashodi, održavanje, zajednica, izvještaji i vlasnički portal. `mobile` je Expo osnova koja koristi isti API. Najnoviji tehnički status je u `FINAL_TECHNICAL_CLOSURE.md`; niži historijski opis odnosi se na stanje u trenutku inicijalnog scaffoldinga.

Finansije V2 Faza 3.1 (`FINANCE_V2_PHASE3_1_STATUS.md`) dodaje append-only `PaymentIdentificationHistory` i zaseban `Payments.ChangePayer` tok. Jedna serijalizirana SQL operacija uz ZEV/Payment lock poništava aktivne alokacije negativnim redovima i tek tada preusmjerava kredit novoj osobi; originalni Payment ne nestaje. Kartica vlasnika koristi SQL agregate po valuti nad cijelim skupom i server-side paginirane događaje s UTC as-of granicom. Billing auto-credit preview i post koriste isti plan i hash, a izdata `Invoice`/PDF snapshot ostaje nepromjenjiv. Raniji opis u ovom dokumentu odražava prethodne faze.

## Historijski kontekst prije Faze 1

Pregledan je cijeli dostavljeni radni direktorij. Sadržavao je samo prazne `work/` i `outputs/` direktorije; nije Git repozitorij. Nema postojeće aplikacije, sheme baze, migracija, authentication sloja, modula, coding conventions ni testova. Zato nema funkcionalnog koda za zadržavanje niti prepoznatog tehničkog duga. Glavni rizik je nedostatak potvrđenih poslovnih i pravnih pravila (`OPEN_BUSINESS_DECISIONS.md`).

## Ciljna arhitektura

- Modularni monolit: Identity/Access, Registry, Billing, Ledger/Funds, Banking, Reconciliation, Expenses, Debt/Legal, Maintenance, Communication/Voting, Documents i Reporting. Moduli dijele transakcijsku SQL Server bazu, ali imaju odvojene aplikacijske servise i eksplicitne API ugovore.
- ASP.NET Core 10 REST API s EF Core 10 i SQL Serverom. React + TypeScript web i React Native mobilni klijent koriste isti API. Poslovna pravila ostaju na serveru.
- `Organization` grupiše ZEV-ove upravnika; svaki `Zev` je zasebna sigurnosna i finansijska granica. `Building`, `Entrance` i `PropertyUnit` su resursi ispod ZEV-a. Korisnik može imati različite uloge po ZEV-u, a OrgAdmin nasljeđuje pristup svim ZEV-ovima svoje organizacije.
- Svaki zahtjev nosi korisnički identitet iz potpisanog access tokena. Ovlaštenje se ponovo provjerava u bazi za konkretan organization/ZEV resurs; ID u URL-u nikad nije dokaz pristupa. Globalni query filteri nisu jedina sigurnosna granica. Za buduće poslovne servise obavezan je tenant-scope query i provjera parent/child pripadnosti prije izmjene.
- Knjižene ledger stavke su nebrisive kroz EF; za rashode postoji povezani reversal. Iznosi su `decimal(18,2)`, stope `decimal(18,4)`, uz trenutnu tehničku politiku zaokruživanja po stavci. Obračun koristi snapshot tarife i jedinstveni ključ `(ZevId, Period)` uz transakciju. Pravna politika zaokruživanja još nije potvrđena.
- Bank import je odvojen od finansijske knjige. Parseri se registruju po formatu; izvodi se dedupliciraju checksumom cijelog fajla. Pojedinačne transakcije nemaju bank ID/fingerprint deduplikaciju. Sravnjenje prvo koristi jedinstveni tačan poziv na broj, a ne fuzzy/AI signal. Nejasni slučajevi idu u ručni red.
- Prilozi idu u file-storage servis; SQL čuva metapodatke i kontrolu pristupa. Audit je append-only i obuhvata ko, kada, tenant, entitet, akciju, prije/poslije i correlation ID.

## Faza 1 kao osnova

`Zev.Api` sadrži identitet, hashirane lozinke, JWT access token i hashirane rotirajuće refresh tokene, eksplicitni `TenantAccess` servis, osnovne tabele za organizaciju/ZEV/članstva, audit SaveChanges sloj, rate limiting i health endpoint. Ova osnova je zadržana; poslovni moduli su naknadno dodani.

## Sigurnosni tok

1. Login provjerava email i hash lozinke; API nikad ne vraća hash.
2. Kratkotrajni JWT sadrži samo `sub` i `jti`; organizacijska/ZEV prava se ne oslanjaju na dugotrajne claimove.
3. Refresh token je nasumičan, u bazi je samo SHA-256 hash; rotacija i opoziv rade transakcijski. Sumnjiva ponovna upotreba starog tokena ne izdaje novi token.
4. Svaki tenant endpoint poziva odgovarajući `TenantAccess.Can*Async`; `EffectiveAccessService` kombinuje sistemsku ulogu, OrgAdmin/ZEV članstvo, verifikovano aktivno vlasništvo i vremenski ograničen manual override, uz prednost `Deny`. SystemAdministrator je globalan. Owner ne dobija redundantno članstvo; `Person.UserId` bez `Verified` ne daje pristup. Detalji i ograničenja su u `ACCESS_CONTROL_AND_PRIVACY.md`.
5. Audit se zapisuje u istoj EF transakciji kao promjena. Osjetljiva polja (lozinke, tokeni) nisu dio snapshot-a.

## Operacije

SQL Server, API i web su odvojeni deployment elementi. Compose uključuje sve tri komponente i trajni storage volume, ali nije end-to-end verificiran u ovom sandboxu. Baza treba dnevni backup, šifriranu pohranu backup-a, periodičan restore test, monitoring health endpointa i jasno zadane RPO/RTO vrijednosti prije produkcije. Cilj dostupnosti 99,5% zahtijeva monitoring i operativne procedure; sam kod ne garantuje SLA. Tajne su environment/secret-store vrijednosti, nikad u repozitoriju. Namjenski SQL test je potvrdio 1001 zaduženje u manje od 30 s.

Acceptance audit 27.09.2026: lokalni `COPY_ONLY` backup, `VERIFYONLY` i restore u novu `ZevRestoreTest` bazu su stvarno izvršeni putem SQL TCP veze (`BACKUP_RESTORE_DRILL.md`), ali off-host restore nije. Raw file endpoint više ne iznosi `ManagedDocument` mimo njegovog permission endpointa; upload i bank import imaju rate limit. As-of otvorene stavke i kartica koriste datum uplate pri računanju alokacija; povrat uplate/storno i pravni tretman ostaju otvoreni.

## Finansijski workflow — 28.09.2026.

**Aktuelno, Faza 3.3 (02.10.2026):** `FinancialReadModel` centralizira valutu `Charge`/`FinancialDocument`/`Invoice`, aktivne uplate i alokacije, UTC as-of granicu, saldo po valuti, status izdatog računa, otvorene stavke i fondovske transakcije. `OwnerCardService` koristi iste primitive za vlasnika, jedinicu, izdati i legacy račun. Neodrediva historijska valuta je `UNKNOWN`, ne podrazumijevani BAM. Globalni izvještaj vraća grupe po valuti i `null` scalar kada je zbir nesiguran. Detalji i ograničenja: `FINANCE_V2_PHASE3_3_STATUS.md`.

**Aktuelno, Payments V2 (01.10.2026):** Novi obračuni imaju trajni `Invoice`/`InvoiceLine` snapshot, dok `Charge` ostaje izvor potraživanja. `Payment` ima bankovni ili ručni izvor; `PaymentAllocation` ostaje jedina veza novca i duga, uključujući negativne append-only ispravke. `PaymentWorkflowService` vodi identifikaciju, raspodjelu, kredit i ručni unos. SQL serijalizacija i zaključavanje ZEV reda štite konkurentne alokacije. `Zev.AutoApplyPaymentCredit` je podrazumijevano isključen; knjiženje može rasporediti kredit poslije izdavanja računa bez promjene PDF snapshot-a. Detalji i neprovjereni SQL koraci su u `FINANCE_V2_PHASE3_STATUS.md`. Tekst od 28.09. ispod je historijski.

`MonthlyBillingEngine` gradi read-only draft plan i fingerprint; post u serijaliziranoj transakciji ponovo gradi isti plan prije upisa `BillingRun` i pojedinačnih `Charge` redova. `TariffVersion` nosi opis, opcionalnost, vidljivost i redoslijed; historijska zaduženja čuvaju snapshot. Račun/PDF/ZIP je projekcija iz `Charge`, a ne zaseban izvor istine. `PaymentReferenceProvider` identifikuje ZEV/jedinicu/osobu. Bank matching prvo koristi tačan poziv pojedinačnog zaduženja, zatim account referencu i najstarije dospijeće u fondu; ne prelazi fondove bez odobrene politike. Puni bankovni povrat koristi negativnu transakciju, `Payment.Status=Reversed`, `Payment.ReversedOn`, `FundTransaction.Reversal` i `ReconciliationAttempt`; originalni redovi ostaju. Aktuelni saldo izuzima stornirane alokacije, dok as-of prije povrata uzima tada važeću uplatu; SQL/UAT i konačno računovodstveno odobrenje još nedostaju. Detalji u `BILLING_AND_PAYMENTS_WORKFLOW.md`.
