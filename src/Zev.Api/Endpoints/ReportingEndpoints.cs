using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;
using Zev.Api.Reporting;
using Zev.Api.Payments;

namespace Zev.Api.Endpoints;

public static class ReportingEndpoints
{
    public static RouteGroupBuilder MapReportingEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/zevs/{zevId:guid}/reports");
        reports.MapGet("/summary", async (Guid zevId, DateOnly? from, DateOnly? to, Guid? fundId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.View", cancellationToken)) return Results.NotFound();
            if (from is not null && to is not null && from > to) return Results.BadRequest();
            if (fundId is Guid selectedFund && !await database.Funds.AnyAsync(item => item.Id == selectedFund
                && item.ZevId == zevId, cancellationToken)) return Results.NotFound();

            var charges = FinancialReadModel.Charges(database, zevId, to);
            var payments = FinancialReadModel.ActivePayments(database, zevId, to);
            var expenses = database.Expenses.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Status == ExpenseStatus.Posted);
            var fundTransactions = FinancialReadModel.FundMovements(database, zevId);
            if (from is DateOnly start)
            {
                charges = charges.Where(item => item.Period >= start);
                payments = payments.Where(item => item.PaidOn >= start);
                expenses = expenses.Where(item => item.InvoiceDate >= start);
                fundTransactions = fundTransactions.Where(item => item.EffectiveOn >= start);
            }
            if (to is DateOnly end)
            {
                charges = charges.Where(item => item.Period <= end);
                payments = payments.Where(item => item.PaidOn <= end);
                expenses = expenses.Where(item => item.InvoiceDate <= end);
                fundTransactions = fundTransactions.Where(item => item.EffectiveOn <= end);
            }
            if (fundId is Guid fund)
            {
                charges = charges.Where(item => item.FundId == fund);
                payments = payments.Where(item => item.FundId == fund);
                expenses = expenses.Where(item => item.FundId == fund);
                fundTransactions = fundTransactions.Where(item => item.FundId == fund);
            }

            var billedByCurrency = await charges.GroupBy(item => item.CurrencyCode)
                .Select(group => new { currencyCode = group.Key,
                    amount = group.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
            var receivedByCurrency = await payments.GroupBy(item => item.CurrencyCode)
                .Select(group => new { currencyCode = group.Key,
                    amount = group.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
            var spentByCurrency = await expenses.GroupBy(item => item.CurrencyCode)
                .Select(group => new { currencyCode = group.Key,
                    amount = group.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
            var funds = await fundTransactions.GroupBy(item => new { item.FundId, item.CurrencyCode })
                .Select(group => new { fundId = group.Key.FundId,
                    currencyCode = group.Key.CurrencyCode, balance = group.Sum(item => item.Amount) })
                .ToListAsync(cancellationToken);
            var currencies = billedByCurrency.Select(item => item.currencyCode)
                .Concat(receivedByCurrency.Select(item => item.currencyCode))
                .Concat(spentByCurrency.Select(item => item.currencyCode))
                .Concat(funds.Select(item => item.currencyCode))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).ToList();
            var balancesByCurrency = currencies.Select(currency =>
            {
                var billed = billedByCurrency.Where(item => item.currencyCode == currency)
                    .Sum(item => item.amount);
                var received = receivedByCurrency.Where(item => item.currencyCode == currency)
                    .Sum(item => item.amount);
                var spent = spentByCurrency.Where(item => item.currencyCode == currency).Sum(item => item.amount);
                return new { currencyCode = currency, billed, received, spent,
                    netCash = received - spent };
            }).ToList();
            var mixedCurrency = currencies.Count > 1 || currencies.Contains(FinancialReadModel.UnknownCurrency);
            var single = !mixedCurrency ? balancesByCurrency.SingleOrDefault() : null;
            return Results.Ok(new { billed = single?.billed, received = single?.received,
                spent = single?.spent, netCash = single?.netCash,
                currencyCode = single?.currencyCode, mixedCurrency,
                balancesByCurrency, fundMovements = funds, from, to, fundId });
        });

        reports.MapGet("/annual/{year:int}", async (Guid zevId, int year, TenantAccess access,
            ZevDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.View", cancellationToken)) return Results.NotFound();
            if (year is < 2000 or > 2100) return Results.BadRequest();
            var (_, currencyProblem) = await AnnualCurrencyAsync(database, zevId, configuration, cancellationToken);
            if (currencyProblem is not null) return currencyProblem;
            return Results.Ok(await BuildAnnualAsync(database, zevId, year, cancellationToken));
        });

        reports.MapGet("/annual/{year:int}.pdf", async (Guid zevId, int year, TenantAccess access,
            ZevDbContext database, IConfiguration configuration, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.Export", cancellationToken)) return Results.NotFound();
            if (year is < 2000 or > 2100) return Results.BadRequest();
            var (_, currencyProblem) = await AnnualCurrencyAsync(database, zevId, configuration, cancellationToken);
            if (currencyProblem is not null) return currencyProblem;
            var report = await BuildAnnualAsync(database, zevId, year, cancellationToken);
            var lines = new List<string> { $"GODISNJI FINANSIJSKI PREGLED {year}",
                "Mjesec     Zaduzena       Uplacena       Rashodi        Neto tok" };
            lines.AddRange(report.Months.Select(item => $"{item.Month,2}   {item.Billed,12:0.00}   {item.Received,12:0.00}   {item.Spent,12:0.00}   {item.NetCash,12:0.00}"));
            await AuditExportAsync(database, httpContext, access.CurrentUserId, zevId,
                "AnnualReport", year.ToString(), new { year }, cancellationToken);
            return Results.File(SimplePdfWriter.Write(lines), "application/pdf", $"godisnji-izvjestaj-{year}.pdf");
        });

        reports.MapGet("/annual/{year:int}.xlsx", async (Guid zevId, int year, TenantAccess access,
            ZevDbContext database, IConfiguration configuration, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.Export", cancellationToken)) return Results.NotFound();
            if (year is < 2000 or > 2100) return Results.BadRequest();
            var (currency, currencyProblem) = await AnnualCurrencyAsync(database, zevId, configuration, cancellationToken);
            if (currencyProblem is not null) return currencyProblem;
            var report = await BuildAnnualAsync(database, zevId, year, cancellationToken);
            var rows = new List<IReadOnlyList<object?>>
            {
                new object?[] { "Mjesec", "Valuta", "Zaduženo", "Uplaćeno", "Rashodi", "Neto tok" }
            };
            rows.AddRange(report.Months.Select(item => (IReadOnlyList<object?>)new object?[]
            {
                item.Month, currency, item.Billed, item.Received, item.Spent, item.NetCash
            }));
            rows.Add(new object?[] { "Ukupno", currency, report.Months.Sum(item => item.Billed),
                report.Months.Sum(item => item.Received), report.Months.Sum(item => item.Spent),
                report.Months.Sum(item => item.NetCash) });
            await AuditExportAsync(database, httpContext, access.CurrentUserId, zevId,
                "AnnualReportXlsx", year.ToString(), new { year }, cancellationToken);
            return Results.File(XlsxWriter.Write(rows),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"godisnji-izvjestaj-{year}.xlsx");
        });

        reports.MapGet("/open-items", async (Guid zevId, DateOnly? asOf, Guid? personId, Guid? buildingId,
            Guid? propertyUnitId, Guid? fundId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.View", cancellationToken)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = FinancialReadModel.OpenItems(database, zevId, date);
            if (personId is Guid person) query = query.Where(item => item.PersonId == person);
            if (buildingId is Guid building) query = query.Where(item => item.BuildingId == building);
            if (propertyUnitId is Guid unit) query = query.Where(item => item.PropertyUnitId == unit);
            if (fundId is Guid fund) query = query.Where(item => item.FundId == fund);
            var outstanding = query.Where(item => item.Amount + item.Correction > item.Allocated);
            var totalCount = await outstanding.CountAsync(cancellationToken);
            var items = await outstanding.OrderBy(item => item.DueDate).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = items.Select(item => new { item.Id, item.PersonId, personName = item.PersonName,
                    item.PropertyUnitId, item.FundId, item.Period, item.DueDate, item.Reference,
                    item.Amount, allocated = item.Allocated, currencyCode = item.CurrencyCode,
                    item.Outstanding,
                    agingDays = Math.Max(0, date.DayNumber - item.DueDate.DayNumber),
                    agingBucket = AgingBucket.For(item.DueDate, date) }) });
        });

        reports.MapGet("/open-items.xlsx", async (Guid zevId, DateOnly? asOf, Guid? personId,
            Guid? buildingId, Guid? propertyUnitId, Guid? fundId, TenantAccess access,
            ZevDbContext database, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Reports.Export", cancellationToken)) return Results.NotFound();
            var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var query = FinancialReadModel.OpenItems(database, zevId, date);
            if (personId is Guid person) query = query.Where(item => item.PersonId == person);
            if (buildingId is Guid building) query = query.Where(item => item.BuildingId == building);
            if (propertyUnitId is Guid unit) query = query.Where(item => item.PropertyUnitId == unit);
            if (fundId is Guid fund) query = query.Where(item => item.FundId == fund);
            var rows = await query.Where(item => item.Amount + item.Correction > item.Allocated)
                .OrderBy(item => item.DueDate).ThenBy(item => item.Id).Take(10001)
                .ToListAsync(cancellationToken);
            if (rows.Count > 10000) return Results.Problem("Export is limited to 10000 open items.", statusCode: 413);
            var table = new List<IReadOnlyList<object?>>
            {
                new object?[] { "Poziv na broj", "Vlasnik", "Jedinica", "Period", "Dospijeće",
                    "Valuta", "Zaduženo", "Uplaćeno", "Otvoreno" }
            };
            table.AddRange(rows.Select(item => (IReadOnlyList<object?>)new object?[]
            {
                item.Reference, item.PersonName, item.UnitCode, item.Period.ToString("yyyy-MM-dd"),
                item.DueDate.ToString("yyyy-MM-dd"), item.CurrencyCode,
                item.Amount + item.Correction, item.Allocated, item.Outstanding
            }));
            await AuditExportAsync(database, httpContext, access.CurrentUserId, zevId,
                "OpenItems", zevId.ToString(), new { asOf = date, personId, buildingId, propertyUnitId,
                    fundId, rowCount = rows.Count }, cancellationToken);
            return Results.File(XlsxWriter.Write(table),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "otvorene-stavke.xlsx");
        });

        reports.MapGet("/owner-card/{personId:guid}", async (Guid zevId, Guid personId,
            DateOnly? from, DateOnly? to, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, OwnerCardService ownerCard,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "OwnerLedger.View", cancellationToken)) return Results.NotFound();
            return await OwnerCardAsync(database, ownerCard, zevId, personId, from, to,
                page, pageSize, cancellationToken);
        });

        api.MapGet("/me/finance/{zevId:guid}", async (Guid zevId, DateOnly? from, DateOnly? to,
            int? page, int? pageSize, TenantAccess access, ZevDbContext database,
            OwnerCardService ownerCard, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId || from > to) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var canRead = await access.HasPermissionAsync(zevId, "Finance.View", cancellationToken, staffOnly: false);
            var personIds = await database.People.AsNoTracking().Where(item => item.UserId == userId
                && item.IsActive && item.UserLinkStatus == UserPersonLinkStatus.Verified)
                .Select(item => item.Id).ToListAsync(cancellationToken);
            var cards = new List<object>();
            foreach (var personId in personIds)
            {
                var activeOwner = canRead && await access.CanAccessOwnPersonAsync(zevId, personId, null,
                    cancellationToken);
                if (!activeOwner && !await access.CanReadHistoricalPersonAsync(zevId, personId, null,
                    cancellationToken)) continue;
                cards.Add(await BuildOwnerCardAsync(database, ownerCard, zevId, personId, from, to,
                    currentPage, currentSize, cancellationToken, historicalOnly: !activeOwner));
            }
            return cards.Count == 0 && !canRead ? Results.NotFound() : Results.Ok(cards);
        });

        return api;
    }

    private static async Task AuditExportAsync(ZevDbContext database, HttpContext httpContext,
        Guid? actorUserId, Guid zevId, string entityType, string entityId, object details,
        CancellationToken cancellationToken)
    {
        var organizationId = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId)
            .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
        database.AuditLogs.Add(new AuditLog { ActorUserId = actorUserId, OrganizationId = organizationId,
            ZevId = zevId, EntityType = entityType, EntityId = entityId, Action = "Export",
            AfterJson = System.Text.Json.JsonSerializer.Serialize(details),
            CorrelationId = httpContext.TraceIdentifier });
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<string>> KnownCurrenciesAsync(ZevDbContext database, Guid zevId,
        CancellationToken cancellationToken)
    {
        var charges = await FinancialReadModel.Charges(database, zevId, null)
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        var payments = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId)
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        var fundCurrencies = await FinancialReadModel.FundMovements(database, zevId)
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken);
        charges.AddRange(await database.Expenses.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Status == ExpenseStatus.Posted)
            .Select(item => item.CurrencyCode).Distinct().ToListAsync(cancellationToken));
        return charges.Concat(payments).Concat(fundCurrencies)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The single currency of the annual report, or a 409 explaining why combined totals are unavailable.
    private static async Task<(string Currency, IResult? Problem)> AnnualCurrencyAsync(ZevDbContext database,
        Guid zevId, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var currencies = await KnownCurrenciesAsync(database, zevId, cancellationToken);
        if (currencies.Contains(FinancialReadModel.UnknownCurrency))
            return (FinancialReadModel.UnknownCurrency, Results.Conflict(new
            {
                error = "Annual totals are unavailable: some records have an unverified (UNKNOWN) currency."
            }));
        if (currencies.Count > 1)
            return (string.Empty, Results.Conflict(new { error = "Annual combined totals are unavailable for multiple currencies." }));
        return (currencies.SingleOrDefault()?.ToUpperInvariant()
            ?? configuration["Finance:DefaultCurrency"]?.Trim().ToUpperInvariant() ?? "BAM", null);
    }

    private static async Task<IResult> OwnerCardAsync(ZevDbContext database, OwnerCardService ownerCard,
        Guid zevId, Guid personId, DateOnly? from, DateOnly? to, int? page, int? pageSize,
        CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to) return Results.BadRequest();
        var organizationId = await database.Zevs.Where(item => item.Id == zevId)
            .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
        if (!await database.People.AnyAsync(item => item.Id == personId && item.OrganizationId == organizationId,
            cancellationToken)) return Results.NotFound();
        var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
        return Results.Ok(await BuildOwnerCardAsync(database, ownerCard, zevId, personId, from, to,
            currentPage, currentSize, cancellationToken));
    }

    private static async Task<object> BuildOwnerCardAsync(ZevDbContext database, OwnerCardService ownerCard,
        Guid zevId, Guid personId, DateOnly? from, DateOnly? to, int page, int pageSize,
        CancellationToken cancellationToken, bool historicalOnly = false)
    {
        var financial = await ownerCard.ReadAsync(zevId, personId, from, to, page, pageSize,
            cancellationToken, historicalOnly);
        var invoiceQuery = database.Invoices.AsNoTracking().Where(item => item.ZevId == zevId
            && item.PersonId == personId);
        if (historicalOnly)
            invoiceQuery = invoiceQuery.Where(item => database.Ownerships.Any(ownership => ownership.ZevId == zevId
                && ownership.PersonId == personId && ownership.PropertyUnitId == item.PropertyUnitId
                && ownership.ValidFrom <= item.Period && (ownership.ValidTo == null || ownership.ValidTo > item.Period)));
        if (from is DateOnly invoiceStart) invoiceQuery = invoiceQuery.Where(item => item.Period >= invoiceStart);
        if (to is DateOnly invoiceEnd)
        {
            var end = FinancialReadModel.EndOfDay(invoiceEnd);
            invoiceQuery = invoiceQuery.Where(item => item.Period <= invoiceEnd && item.CreatedUtc < end);
        }
        var invoiceRows = await invoiceQuery.OrderBy(item => item.Period).ThenBy(item => item.NumberSequence)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new { item.Id, item.PropertyUnitId, item.InvoiceNumber, item.Period,
                item.IssueDate, item.DueDate, item.TotalAmount, item.DocumentStatus, item.Currency })
            .ToListAsync(cancellationToken);
        var invoiceIds = invoiceRows.Select(item => item.Id).ToArray();
        var invoiceStates = await FinancialReadModel.InvoiceStatesAsync(database, zevId, invoiceIds,
            to, cancellationToken);
        var documentEnd = FinancialReadModel.EndOfDay(to);
        var documents = await database.FinancialDocuments.AsNoTracking()
            .Where(item => item.ZevId == zevId && invoiceIds.Contains(item.OriginalInvoiceId)
                && (documentEnd == null || item.CreatedUtc < documentEnd))
            .OrderBy(item => item.IssueDate).Select(item => new { item.Id, item.OriginalInvoiceId,
                item.DocumentNumber, type = item.Type.ToString(), item.IssueDate, item.SignedAmount,
                item.Currency,
                item.Reason }).ToListAsync(cancellationToken);
        var singleCurrency = financial.Balances.Count == 1 ? financial.Balances[0] : null;
        return new { personId, financial.Balances, groupedByCurrency = financial.Balances,
            financial.Events, financial.Page, financial.PageSize, financial.TotalCount,
            invoices = invoiceRows.Select(item =>
            {
                var state = invoiceStates[item.Id];
                var current = state.Balances.SingleOrDefault(balance =>
                    string.Equals(balance.CurrencyCode, item.Currency, StringComparison.OrdinalIgnoreCase));
                var effectiveAmount = current?.TotalCharges ?? 0m;
                var paid = current?.TotalPayments ?? 0m;
                var relatedDocuments = documents.Where(document => document.OriginalInvoiceId == item.Id).ToList();
                var documentStatus = to is null ? item.DocumentStatus.ToString()
                    : relatedDocuments.Any(document => document.type == "Storno") ? "Cancelled"
                    : relatedDocuments.Count > 0 ? "Corrected" : "Issued";
                return new { item.Id, item.PropertyUnitId, item.InvoiceNumber, item.Period,
                    item.IssueDate, item.DueDate, amount = item.TotalAmount, paid, item.Currency,
                    paidBeforeStorno = state.PaidBeforeStorno, stornoCredit = state.StornoCredit,
                    correctionAmount = effectiveAmount - item.TotalAmount, effectiveAmount,
                    open = current?.Open ?? 0m, documentStatus,
                    paymentStatus = effectiveAmount <= 0m || paid >= effectiveAmount ? "Paid"
                        : paid <= 0m ? "Unpaid" : "PartiallyPaid" };
            }),
            documents,
            totalCharged = singleCurrency?.TotalCharges, totalPaid = singleCurrency?.TotalPayments,
            balance = singleCurrency?.NetBalance };
    }

    private sealed record AnnualMonth(int Month, decimal Billed, decimal Received, decimal Spent)
    {
        public decimal NetCash => Received - Spent;
    }

    private sealed record AnnualReport(int Year, IReadOnlyList<AnnualMonth> Months);

    private static async Task<AnnualReport> BuildAnnualAsync(ZevDbContext database, Guid zevId, int year,
        CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        var end = start.AddYears(1);
        var billed = await database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
                && (item.EffectiveOn ?? item.Period) >= start
                && (item.EffectiveOn ?? item.Period) < end)
            .GroupBy(item => (item.EffectiveOn ?? item.Period).Month).Select(group => new { month = group.Key,
                amount = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.month, item => item.amount,
                cancellationToken);
        var received = await database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
                && item.PaidOn >= start && item.PaidOn < end)
            .GroupBy(item => item.PaidOn.Month).Select(group => new { month = group.Key,
                amount = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.month, item => item.amount,
                cancellationToken);
        // Only bank returns of payments reduce "received"; an expense reversal is not a payment.
        var returns = await database.FundTransactions.AsNoTracking().Where(item => item.ZevId == zevId
                && item.Kind == FundTransactionKind.Reversal && item.ReversesTransaction != null
                && item.ReversesTransaction.Kind == FundTransactionKind.Payment
                && item.EffectiveOn >= start && item.EffectiveOn < end)
            .GroupBy(item => item.EffectiveOn.Month).Select(group => new { month = group.Key,
                amount = group.Sum(item => item.SignedAmount) }).ToDictionaryAsync(item => item.month,
                item => item.amount, cancellationToken);
        // Expenses from the fund ledger: posting (negative) counts in its month, a reversal (positive) reduces
        // spending in the month it was reversed, so history is not rewritten.
        var spent = await database.FundTransactions.AsNoTracking().Where(item => item.ZevId == zevId
                && (item.Kind == FundTransactionKind.Expense
                    || item.Kind == FundTransactionKind.Reversal && item.ReversesTransaction != null
                        && item.ReversesTransaction.Kind == FundTransactionKind.Expense)
                && item.EffectiveOn >= start && item.EffectiveOn < end)
            .GroupBy(item => item.EffectiveOn.Month).Select(group => new { month = group.Key,
                amount = -group.Sum(item => item.SignedAmount) }).ToDictionaryAsync(item => item.month,
                item => item.amount, cancellationToken);
        return new AnnualReport(year, Enumerable.Range(1, 12).Select(month => new AnnualMonth(month,
            billed.GetValueOrDefault(month), received.GetValueOrDefault(month) + returns.GetValueOrDefault(month),
            spent.GetValueOrDefault(month))).ToList());
    }
}
