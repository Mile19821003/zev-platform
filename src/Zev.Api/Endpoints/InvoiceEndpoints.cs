using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Payments;
using Zev.Api.Reporting;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class InvoiceEndpoints
{
    public static RouteGroupBuilder MapInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var invoices = api.MapGroup("/zevs/{zevId:guid}/finance/invoices");
        invoices.MapGet("", async (Guid zevId, DateOnly period, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Invoice.View", cancellationToken) is { } denied) return denied;
            if (period.Day != 1) return Results.BadRequest();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var issued = await database.Invoices.AsNoTracking().Where(item => item.ZevId == zevId && item.Period == period)
                .OrderBy(item => item.NumberSequence).Select(item => new
                {
                    item.Id, item.PropertyUnitId, item.PersonId, unitCode = item.UnitCodeSnapshot,
                    personName = item.PersonNameSnapshot, item.InvoiceNumber, item.IssueDate, item.DueDate,
                    total = item.TotalAmount, item.Currency,
                    documentStatus = item.DocumentStatus.ToString(),
                    count = item.Lines.Count
                }).ToListAsync(cancellationToken);
            var issuedIds = issued.Select(item => item.Id).ToArray();
            var states = await FinancialReadModel.InvoiceStatesAsync(database, zevId, issuedIds,
                null, cancellationToken);
            var legacyFacts = FinancialReadModel.Charges(database, zevId, null)
                .Where(item => item.Period == period && item.Kind == "LegacyCharge");
            var legacyCharges = await (from fact in legacyFacts
                join charge in database.Charges.AsNoTracking() on fact.Id equals charge.Id
                select new { fact.Id, fact.PropertyUnitId, fact.PersonId,
                    unitCode = charge.PropertyUnit.Code, personName = charge.Person.DisplayName,
                    fact.Amount, fact.CurrencyCode }).ToListAsync(cancellationToken);
            var legacyPayments = await (from allocation in FinancialReadModel.ActiveAllocations(database, zevId, null)
                join charge in legacyFacts on allocation.ChargeId equals charge.Id
                select new { allocation.ChargeId, allocation.Amount })
                .GroupBy(item => item.ChargeId)
                .Select(group => new { Id = group.Key, Paid = group.Sum(item => item.Amount) })
                .ToDictionaryAsync(item => item.Id, item => item.Paid, cancellationToken);
            var legacy = legacyCharges.GroupBy(item => new { item.PropertyUnitId, item.PersonId,
                    item.unitCode, item.personName, item.CurrencyCode })
                .Select(group => new { group.Key.PropertyUnitId, group.Key.PersonId, group.Key.unitCode,
                    group.Key.personName, group.Key.CurrencyCode,
                    total = group.Sum(item => item.Amount), count = group.Count(),
                    paid = group.Sum(item => legacyPayments.GetValueOrDefault(item.Id)) }).ToList();
            var itemsAll = issued.Select(item => new InvoiceListItem(item.Id, item.PropertyUnitId, item.PersonId,
                    item.unitCode, item.personName, item.total, item.count, item.InvoiceNumber,
                    item.IssueDate, item.DueDate,
                    states[item.Id].Balances.SingleOrDefault(balance => balance.CurrencyCode == item.Currency)
                        ?.TotalPayments ?? 0m, item.documentStatus)
                { CurrencyCode = item.Currency,
                    EffectiveTotal = states[item.Id].Balances.SingleOrDefault(balance =>
                        balance.CurrencyCode == item.Currency)?.TotalCharges ?? 0m })
                .Concat(legacy.Select(item => new InvoiceListItem(null, item.PropertyUnitId, item.PersonId,
                    item.unitCode, item.personName, item.total, item.count, null, null, null,
                    item.paid, "Legacy") { CurrencyCode = item.CurrencyCode }))
                .OrderBy(item => item.UnitCode).ThenBy(item => item.PersonName).ToList();
            var totalCount = itemsAll.Count;
            var items = itemsAll.Skip((currentPage - 1) * currentSize).Take(currentSize).ToList();
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        invoices.MapGet("/{unitId:guid}/{personId:guid}", async (Guid zevId, Guid unitId, Guid personId,
            DateOnly period, TenantAccess access, ZevDbContext database,
            PaymentReferenceService references, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await CanReadInvoiceAsync(access, zevId, personId, period, cancellationToken))
                return Results.NotFound();
            var invoice = await ReadAsync(database, references, ownerCard, zevId, unitId, personId,
                period, cancellationToken);
            if (invoice?.IsMixedCurrency == true) return Results.Conflict(new
            { error = "Legacy billing contains multiple currencies; use currency-separated entries.",
                invoice.BalancesByCurrency });
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        });

        invoices.MapGet("/{unitId:guid}/{personId:guid}.pdf", async (Guid zevId, Guid unitId, Guid personId,
            DateOnly period, TenantAccess access, ZevDbContext database,
            PaymentReferenceService references, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await CanReadInvoiceAsync(access, zevId, personId, period, cancellationToken))
                return Results.NotFound();
            var invoice = await ReadAsync(database, references, ownerCard, zevId, unitId, personId,
                period, cancellationToken, includeOwnerBalance: false);
            if (invoice?.IsMixedCurrency == true) return Results.Conflict(new
            { error = "Legacy billing contains multiple currencies; PDF cannot combine them.",
                invoice.BalancesByCurrency });
            if (invoice is null) return Results.NotFound();
            return Results.File(RenderPdf(invoice), "application/pdf", $"racun-{unitId:N}-{period:yyyyMM}.pdf");
        });

        invoices.MapGet("/{unitId:guid}/{personId:guid}/payment-slip.pdf", async (Guid zevId,
            Guid unitId, Guid personId, DateOnly period, Guid fundId, TenantAccess access,
            ZevDbContext database, PaymentReferenceService references, IPaymentSlipProvider slips,
            OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await CanReadInvoiceAsync(access, zevId, personId, period, cancellationToken))
                return Results.NotFound();
            var invoice = await ReadAsync(database, references, ownerCard, zevId, unitId, personId,
                period, cancellationToken, includeOwnerBalance: false);
            if (invoice?.IsMixedCurrency == true || invoice?.Currency == FinancialReadModel.UnknownCurrency)
                return Results.Conflict(new { error = "Payment slip requires a verified single currency." });
            if (invoice is null) return Results.NotFound();
            if (invoice.DocumentStatus == "Cancelled")
                return Results.Conflict(new { error = "Cancelled invoices do not have payable slips." });
            var account = invoice.Accounts.SingleOrDefault(item => item.FundId == fundId);
            var lines = invoice.Lines.Where(item => item.FundId == fundId).ToList();
            if (account is null || lines.Count == 0) return Results.Conflict();
            var issuedId = await database.Invoices.AsNoTracking().Where(item => item.ZevId == zevId
                    && item.PropertyUnitId == unitId && item.PersonId == personId && item.Period == period)
                .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
            var correctionChargeIds = issuedId is Guid originalId
                ? await database.FinancialDocumentLines.AsNoTracking()
                    .Where(item => item.Document.OriginalInvoiceId == originalId && item.FundId == fundId)
                    .Select(item => item.ChargeId).ToArrayAsync(cancellationToken) : [];
            var chargeIds = lines.Select(item => item.ChargeId).Concat(correctionChargeIds).ToArray();
            var fundBalances = await FinancialReadModel.ChargeIdsAsync(database, zevId, chargeIds,
                null, cancellationToken);
            if (fundBalances.Count != 1 || fundBalances[0].CurrencyCode != account.CurrencyCode)
                return Results.Conflict(new { error = "Payment slip requires a verified fund currency." });
            var outstanding = fundBalances[0].Open;
            if (outstanding <= 0m) return Results.Conflict(new { error = "Fund charges are already settled." });
            var pdf = slips.Generate(new PaymentSlipData(invoice.ZevName, account.AccountNumber,
                invoice.PersonName, invoice.PaymentReference, outstanding, account.CurrencyCode,
                lines.Max(item => item.DueDate), $"Zaduzenje {period:MM.yyyy} - {account.FundName}"));
            return Results.File(pdf, "application/pdf", $"uplatnica-{unitId:N}-{fundId:N}-{period:yyyyMM}.pdf");
        });

        invoices.MapGet("/archive.zip", async (Guid zevId, DateOnly period, TenantAccess access,
            ZevDbContext database, PaymentReferenceService references, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Invoice.Export", cancellationToken) is { } denied) return denied;
            if (period.Day != 1) return Results.BadRequest();
            var keys = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                    && item.Period == period).Select(item => new { item.PropertyUnitId, item.PersonId })
                .Distinct().Take(1001).ToListAsync(cancellationToken);
            if (keys.Count > 1000) return Results.Problem("Archive is limited to 1000 invoices.", statusCode: 413);
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (var key in keys)
                {
                    var invoice = await ReadAsync(database, references, ownerCard, zevId, key.PropertyUnitId,
                        key.PersonId, period, cancellationToken, includeOwnerBalance: false);
                    if (invoice?.IsMixedCurrency == true) return Results.Conflict(new
                    { error = "Legacy billing contains multiple currencies; archive cannot combine them." });
                    if (invoice is null) continue;
                    var entry = archive.CreateEntry($"racun-{key.PropertyUnitId:N}-{key.PersonId:N}.pdf");
                    await using var output = entry.Open();
                    await output.WriteAsync(RenderPdf(invoice), cancellationToken);
                }
            }
            return Results.File(stream.ToArray(), "application/zip", $"racuni-{period:yyyyMM}.zip");
        });
        return api;
    }

    internal static async Task<bool> CanReadInvoiceAsync(TenantAccess access,
        Guid zevId, Guid personId, DateOnly period, CancellationToken cancellationToken)
    {
        if (await access.CanReadZevAsync(zevId, cancellationToken))
            return await access.HasPermissionAsync(zevId, "Invoice.View", cancellationToken)
                || await access.HasPermissionAsync(zevId, "Finance.View", cancellationToken, staffOnly: false)
                    && await access.CanAccessOwnPersonAsync(zevId, personId, period, cancellationToken);
        return await access.CanReadHistoricalPersonAsync(zevId, personId, period, cancellationToken);
    }

    private static async Task<InvoiceView?> ReadAsync(ZevDbContext database, PaymentReferenceService references,
        OwnerCardService ownerCard,
        Guid zevId, Guid unitId, Guid personId, DateOnly period, CancellationToken cancellationToken,
        bool includeOwnerBalance = true)
    {
        if (period.Day != 1) return null;
        var issued = await database.Invoices.AsNoTracking().Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.ZevId == zevId && item.PropertyUnitId == unitId
                && item.PersonId == personId && item.Period == period, cancellationToken);
        if (issued is not null)
        {
            var chargeIds = issued.Lines.Select(item => item.ChargeId).ToArray();
            var corrections = await database.FinancialDocuments.AsNoTracking()
                .Where(item => item.ZevId == zevId && item.OriginalInvoiceId == issued.Id)
                .Select(item => new { item.SignedAmount, item.Id, item.Type }).ToListAsync(cancellationToken);
            var correctionIds = corrections.Select(item => item.Id).ToArray();
            var correctionChargeIds = await database.FinancialDocumentLines.AsNoTracking()
                .Where(item => correctionIds.Contains(item.DocumentId)).Select(item => item.ChargeId)
                .ToArrayAsync(cancellationToken);
            var allChargeIds = chargeIds.Concat(correctionChargeIds).ToArray();
            var allocations = await database.PaymentAllocations.AsNoTracking()
                .Where(item => allChargeIds.Contains(item.ChargeId) && item.Payment.Status == Domain.PaymentStatus.Posted)
                .OrderBy(item => item.Payment.PaidOn)
                .Select(item => new InvoicePayment(item.PaymentId, item.ChargeId, item.Payment.PaidOn,
                    item.Amount, item.FinancialDocumentId))
                .ToListAsync(cancellationToken);
            var issuedAccounts = issued.Lines.Where(item => !string.IsNullOrWhiteSpace(item.BankAccountSnapshot))
                .GroupBy(item => item.FundId).Select(group => new InvoiceAccount(group.Key,
                    group.First().FundNameSnapshot, group.First().BankAccountSnapshot!, issued.Currency,
                    group.Sum(item => item.FinalAmount))).ToList();
            var lines = issued.Lines.OrderBy(item => item.SortOrder).ThenBy(item => item.CodeSnapshot)
                .Select(item => new InvoiceLine(item.ChargeId, item.CodeSnapshot, item.DescriptionSnapshot,
                    item.FundId, item.FinalAmount, string.Empty, item.ShowOnInvoice, item.NoteSnapshot,
                    issued.DueDate, item.UnitOfMeasureSnapshot, item.Quantity, item.UnitPrice,
                    item.DiscountType.ToString(), item.DiscountValue, item.DiscountAmount,
                    item.AmountBeforeDiscount) { Id = item.Id }).ToList();
            var financial = await ownerCard.ReadInvoiceAsync(zevId, issued.Id, null, cancellationToken);
            var current = financial.Balances.SingleOrDefault(item => string.Equals(item.CurrencyCode,
                issued.Currency, StringComparison.OrdinalIgnoreCase));
            var issuedPaid = current?.TotalPayments ?? 0m;
            var effectiveTotal = current?.TotalCharges ?? 0m;
            var invoiceState = (await FinancialReadModel.InvoiceStatesAsync(database, zevId,
                [issued.Id], null, cancellationToken))[issued.Id];
            var stornoCredit = invoiceState.StornoCredit;
            var paidBeforeStorno = invoiceState.PaidBeforeStorno;
            var credit = 0m;
            if (includeOwnerBalance)
            {
                var balance = await ownerCard.ReadAsync(zevId, personId, null, null, 1, 1,
                    cancellationToken, balancesOnly: true);
                credit = balance.Balances.SingleOrDefault(item =>
                    string.Equals(item.CurrencyCode, issued.Currency, StringComparison.OrdinalIgnoreCase))?.Credit ?? 0m;
            }
            return new(zevId, unitId, personId, period, issued.ZevNameSnapshot,
                issued.ZevAddressSnapshot, issued.UnitCodeSnapshot, issued.UnitAreaSnapshot,
                issued.PersonNameSnapshot, lines, issued.TotalAmount, issuedPaid,
                issued.PreviousBalanceSnapshot, current?.Open ?? 0m,
                issued.PaymentReferenceSnapshot, issuedAccounts)
            {
                InvoiceNumber = issued.InvoiceNumber, IssueDate = issued.IssueDate, DueDate = issued.DueDate,
                DocumentStatus = issued.DocumentStatus.ToString(), Currency = issued.Currency,
                TotalDueAtIssue = issued.TotalDueAtIssue, DiscountTotal = issued.DiscountTotal,
                ZevTaxIdentifier = issued.ZevTaxIdentifierSnapshot, ZevEmail = issued.ZevEmailSnapshot,
                ZevPhone = issued.ZevPhoneSnapshot, PersonAddress = issued.PersonAddressSnapshot,
                UnitType = issued.UnitTypeSnapshot, UnitAddress = issued.UnitAddressSnapshot,
                Payments = allocations, Credit = credit, EffectiveTotal = effectiveTotal,
                PaidBeforeStorno = paidBeforeStorno, StornoCredit = stornoCredit
            };
        }
        var charges = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                && item.PropertyUnitId == unitId && item.PersonId == personId && item.Period == period
                && item.FinancialDocumentId == null)
            .OrderBy(item => item.TariffVersion != null ? item.TariffVersion.DisplayOrder : int.MaxValue)
            .ThenBy(item => item.TariffVersion != null ? item.TariffVersion.Code : "MANUAL")
            .Select(item => new InvoiceLine(item.Id, item.TariffVersion != null ? item.TariffVersion.Code : "MANUAL",
                item.TariffVersion != null ? item.TariffVersion.Name : item.Note ?? "Ručna stavka",
                item.FundId, item.Amount, item.Reference, item.TariffVersion == null || item.TariffVersion.ShowOnInvoice,
                item.Note, item.DueDate)).ToListAsync(cancellationToken);
        if (charges.Count == 0) return null;
        var details = await database.PropertyUnits.AsNoTracking().Where(item => item.Id == unitId && item.ZevId == zevId)
            .Select(item => new { item.Code, item.AreaSquareMeters, zevName = item.Entrance.Building.Zev.Name,
                zevAddress = item.Entrance.Building.Zev.AddressLine }).SingleAsync(cancellationToken);
        var owner = await database.People.AsNoTracking().Where(item => item.Id == personId)
            .Select(item => item.DisplayName).SingleAsync(cancellationToken);
        var currentState = await ownerCard.ReadLegacyAsync(zevId, unitId, personId, period,
            null, cancellationToken);
        var activeBalances = currentState.Balances.Where(item => item.TotalCharges != 0m
            || item.TotalPayments != 0m).ToList();
        if (activeBalances.Count > 1)
            return new InvoiceView(zevId, unitId, personId, period, details.zevName,
                details.zevAddress, details.Code, details.AreaSquareMeters, owner,
                charges, 0m, 0m, 0m, 0m, await references.EnsureAsync(zevId, unitId, personId, cancellationToken), [])
            { IsMixedCurrency = true, BalancesByCurrency = activeBalances };
        var legacyBalance = activeBalances.SingleOrDefault();
        var currency = legacyBalance?.CurrencyCode ?? FinancialReadModel.UnknownCurrency;
        var priorState = await ownerCard.ReadUnitAsync(zevId, unitId, personId, null,
            period.AddDays(-1), 1, 1, cancellationToken, balancesOnly: true);
        var previousOpen = priorState.Balances.SingleOrDefault(item => item.CurrencyCode == currency)?.Open ?? 0m;
        var fundIds = charges.Select(item => item.FundId).Distinct().ToArray();
        var bankAccounts = await database.BankAccounts.AsNoTracking().Where(item => item.ZevId == zevId
                && fundIds.Contains(item.FundId) && item.IsActive)
            .Select(item => new { item.FundId, fundName = item.Fund.Name,
                item.AccountNumber, item.CurrencyCode })
            .ToListAsync(cancellationToken);
        var accounts = bankAccounts.Where(item => item.CurrencyCode == currency)
            .GroupBy(item => item.FundId).Select(group =>
        {
            var account = group.OrderBy(item => item.AccountNumber).First();
            return new InvoiceAccount(group.Key, account.fundName, account.AccountNumber,
                account.CurrencyCode, charges.Where(item => item.FundId == group.Key).Sum(item => item.Amount));
        }).ToList();
        var total = legacyBalance?.TotalCharges ?? 0m;
        var currentPaid = legacyBalance?.TotalPayments ?? 0m;
        return new InvoiceView(zevId, unitId, personId, period, details.zevName, details.zevAddress,
            details.Code, details.AreaSquareMeters, owner, charges, total, currentPaid, previousOpen,
            (legacyBalance?.Open ?? 0m) + previousOpen,
            await references.EnsureAsync(zevId, unitId, personId, cancellationToken), accounts)
        {
            Currency = currency, TotalDueAtIssue = total + previousOpen,
            BalancesByCurrency = currentState.Balances,
            DueDate = charges.Max(item => item.DueDate)
        };
    }

    private static byte[] RenderPdf(InvoiceView invoice)
    {
        if (invoice.InvoiceNumber is not null)
        {
            var issuedLines = new List<string>
            {
                $"RACUN {invoice.InvoiceNumber}",
                $"ZEV: {invoice.ZevName} / {invoice.ZevAddress}",
                $"JIB: {invoice.ZevTaxIdentifier ?? "-"}   Kontakt: {invoice.ZevEmail ?? invoice.ZevPhone ?? "-"}",
                $"Primalac: {invoice.PersonName} / {invoice.PersonAddress ?? "-"}",
                $"Jedinica: {invoice.UnitCode} ({invoice.UnitType}, {invoice.AreaSquareMeters:0.###} m2)",
                $"Lokacija: {invoice.UnitAddress ?? "-"}",
                $"Period: {invoice.Period:MM.yyyy}  Izdato: {invoice.IssueDate:dd.MM.yyyy}  Dospijece: {invoice.DueDate:dd.MM.yyyy}",
                "Br. | Opis | JM | Kolicina | Cijena | Popust | Iznos"
            };
            var lineNumber = 0;
            issuedLines.AddRange(invoice.Lines.Where(item => item.ShowOnInvoice).Select(item =>
                $"{++lineNumber}. {item.Name} | {item.UnitOfMeasure} | {item.Quantity:0.###} | {item.UnitPrice:0.####} | {item.DiscountAmount:0.00} | {item.Amount:0.00}"));
            var hiddenTotal = invoice.Lines.Where(item => !item.ShowOnInvoice).Sum(item => item.Amount);
            if (hiddenTotal != 0m) issuedLines.Add($"Ostale stavke: {hiddenTotal:0.00} {invoice.Currency}");
            issuedLines.Add($"Prethodni saldo: {invoice.PreviousOpen:0.00} {invoice.Currency}");
            issuedLines.Add($"Popust: {invoice.DiscountTotal:0.00} {invoice.Currency}");
            issuedLines.Add($"Novo zaduzenje: {invoice.CurrentTotal:0.00} {invoice.Currency}");
            issuedLines.Add($"Ukupno za uplatu pri izdavanju: {invoice.TotalDueAtIssue:0.00} {invoice.Currency}");
            foreach (var account in invoice.Accounts)
                issuedLines.Add($"{account.FundName}: {account.AccountNumber} ({account.CurrencyCode})");
            issuedLines.Add($"Svrha: Obaveze za {invoice.Period:MM.yyyy}; poziv na broj: {invoice.PaymentReference}");
            return SimplePdfWriter.Write(issuedLines);
        }
        var lines = new List<string> { "INFORMATIVNI MJESECNI RACUN - nije odobreni zakonski obrazac",
            $"ZEV: {invoice.ZevName} / {invoice.ZevAddress}",
            $"Period: {invoice.Period:MM.yyyy}  Jedinica: {invoice.UnitCode} ({invoice.AreaSquareMeters} m2)",
            $"Vlasnik: {invoice.PersonName}", "Stavke:" };
        lines.AddRange(invoice.Lines.Where(item => item.ShowOnInvoice).Select(item =>
            $"{item.Name}: {item.Amount.ToString("0.00", CultureInfo.InvariantCulture)}"));
        var hidden = invoice.Lines.Where(item => !item.ShowOnInvoice).Sum(item => item.Amount);
        if (hidden > 0m) lines.Add($"Ostala zaduženja: {hidden.ToString("0.00", CultureInfo.InvariantCulture)}");
        var legacyCurrency = invoice.Currency == FinancialReadModel.UnknownCurrency
            ? "[valuta nepoznata]" : invoice.Currency;
        lines.Add($"Tekuce zaduzenje: {invoice.CurrentTotal.ToString("0.00", CultureInfo.InvariantCulture)} {legacyCurrency}");
        lines.Add($"Uplaceno za ovaj period: {invoice.CurrentPaid.ToString("0.00", CultureInfo.InvariantCulture)} {legacyCurrency}");
        lines.Add($"Prethodni otvoreni saldo: {invoice.PreviousOpen.ToString("0.00", CultureInfo.InvariantCulture)} {legacyCurrency}");
        lines.Add($"Ukupno otvoreno: {invoice.TotalOpen.ToString("0.00", CultureInfo.InvariantCulture)} {legacyCurrency}");
        foreach (var account in invoice.Accounts)
            lines.Add($"{account.FundName}: {account.CurrentAmount:0.00} -> {account.AccountNumber} {account.CurrencyCode}");
        lines.Add($"Poziv na broj: {invoice.PaymentReference}");
        return SimplePdfWriter.Write(lines);
    }
}

public sealed record InvoiceLine(Guid ChargeId, string Code, string Name, Guid FundId, decimal Amount,
    string ChargeReference, bool ShowOnInvoice, string? Note, DateOnly DueDate,
    string? UnitOfMeasure = null, decimal? Quantity = null, decimal? UnitPrice = null,
    string? DiscountType = null, decimal? DiscountValue = null, decimal? DiscountAmount = null,
    decimal? AmountBeforeDiscount = null)
{
    public Guid? Id { get; init; }
}
public sealed record InvoiceAccount(Guid FundId, string FundName, string AccountNumber,
    string CurrencyCode, decimal CurrentAmount);
public sealed record InvoiceView(Guid ZevId, Guid PropertyUnitId, Guid PersonId, DateOnly Period,
    string ZevName, string? ZevAddress, string UnitCode, decimal AreaSquareMeters, string PersonName,
    IReadOnlyList<InvoiceLine> Lines, decimal CurrentTotal, decimal CurrentPaid, decimal PreviousOpen, decimal TotalOpen,
    string PaymentReference, IReadOnlyList<InvoiceAccount> Accounts)
{
    public bool IsMixedCurrency { get; init; }
    public IReadOnlyList<OwnerCurrencyBalance> BalancesByCurrency { get; init; } = [];
    public string? InvoiceNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public string DocumentStatus { get; init; } = "Legacy";
    public string Currency { get; init; } = FinancialReadModel.UnknownCurrency;
    public decimal TotalDueAtIssue { get; init; }
    public decimal DiscountTotal { get; init; }
    public string? ZevTaxIdentifier { get; init; }
    public string? ZevEmail { get; init; }
    public string? ZevPhone { get; init; }
    public string? PersonAddress { get; init; }
    public string? UnitType { get; init; }
    public string? UnitAddress { get; init; }
    public IReadOnlyList<InvoicePayment> Payments { get; init; } = [];
    public decimal Credit { get; init; }
    public decimal? EffectiveTotal { get; init; }
    public decimal PaidBeforeStorno { get; init; }
    public decimal StornoCredit { get; init; }
    public string PaymentStatus => (EffectiveTotal ?? CurrentTotal) <= 0m || CurrentPaid >= (EffectiveTotal ?? CurrentTotal)
        ? Credit > 0m ? "Credit" : "Paid"
        : CurrentPaid <= 0m ? "Unpaid" : "PartiallyPaid";
}
public sealed record InvoicePayment(Guid PaymentId, Guid ChargeId, DateOnly PaidOn, decimal Amount,
    Guid? FinancialDocumentId = null);
public sealed record InvoiceListItem(Guid? Id, Guid PropertyUnitId, Guid PersonId, string UnitCode,
    string PersonName, decimal Total, int Count, string? InvoiceNumber, DateOnly? IssueDate,
    DateOnly? DueDate, decimal Paid, string DocumentStatus)
{
    public string CurrencyCode { get; init; } = FinancialReadModel.UnknownCurrency;
    public decimal? EffectiveTotal { get; init; }
    public decimal Open => Math.Max(0m, FinancialReadModel.CalculateChargeOpen(
        EffectiveTotal ?? Total, Paid));
    public string PaymentStatus => (EffectiveTotal ?? Total) <= 0m || Open <= 0m ? "Paid"
        : Paid <= 0m ? "Unpaid" : "PartiallyPaid";
}
