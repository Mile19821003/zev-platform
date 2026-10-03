using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed record OwnerCurrencyBalance(string CurrencyCode, decimal TotalCharges, decimal TotalPayments,
    decimal Open, decimal Credit, decimal NetBalance);
public sealed record OwnerLedgerEvent(DateOnly Date, string Kind, string Document, string Description,
    string CurrencyCode, decimal Debt, decimal Payment, decimal CreditDelta,
    Guid? ChargeId, Guid? PaymentId)
{
    public decimal RunningBalance { get; init; }
    public Guid? PersonId { get; init; }
    public Guid? PropertyUnitId { get; init; }
    public DateOnly? Period { get; init; }
}
public sealed record OwnerCardResult(IReadOnlyList<OwnerCurrencyBalance> Balances, int Page, int PageSize,
    int TotalCount, IReadOnlyList<OwnerLedgerEvent> Events);

public sealed class OwnerCardService
{
    private readonly ZevDbContext database;

    public OwnerCardService(ZevDbContext database, IConfiguration configuration)
    {
        this.database = database;
    }

    public Task<OwnerCardResult> ReadAsync(Guid zevId, Guid personId, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken cancellationToken, bool historicalOnly = false,
        bool balancesOnly = false) => ReadCoreAsync(zevId, personId, null, null, null,
            from, to, page, pageSize, cancellationToken, historicalOnly, balancesOnly);

    public Task<OwnerCardResult> ReadUnitAsync(Guid zevId, Guid unitId, Guid? personId,
        DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken,
        bool balancesOnly = false) =>
        ReadCoreAsync(zevId, personId, unitId, null, null, from, to, page, pageSize,
            cancellationToken, false, balancesOnly);

    public Task<OwnerCardResult> ReadInvoiceAsync(Guid zevId, Guid invoiceId, DateOnly? to,
        CancellationToken cancellationToken) => ReadCoreAsync(zevId, null, null, invoiceId, null,
            null, to, 1, 1, cancellationToken, false, true);

    public Task<OwnerCardResult> ReadLegacyAsync(Guid zevId, Guid unitId, Guid personId,
        DateOnly period, DateOnly? to, CancellationToken cancellationToken) =>
        ReadCoreAsync(zevId, personId, unitId, null, period, null, to, 1, 1,
            cancellationToken, false, true);

    private async Task<OwnerCardResult> ReadCoreAsync(Guid zevId, Guid? personId, Guid? unitId,
        Guid? invoiceId, DateOnly? legacyPeriod, DateOnly? from, DateOnly? to, int page, int pageSize,
        CancellationToken cancellationToken, bool historicalOnly, bool balancesOnly)
    {
        var end = FinancialReadModel.EndOfDay(to);
        var start = from is DateOnly startDate
            ? new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            : (DateTimeOffset?)null;
        var ownerScope = unitId is null && invoiceId is null && legacyPeriod is null;
        var charges = FinancialReadModel.Charges(database, zevId, to);
        if (personId is Guid selectedPerson) charges = charges.Where(item => item.PersonId == selectedPerson);
        if (unitId is Guid selectedUnit) charges = charges.Where(item => item.PropertyUnitId == selectedUnit);
        if (invoiceId is Guid selectedInvoice)
            charges = charges.Where(item => item.InvoiceId == selectedInvoice
                || item.OriginalInvoiceId == selectedInvoice);
        if (legacyPeriod is DateOnly selectedPeriod)
            charges = charges.Where(item => item.Period == selectedPeriod && item.Kind == "LegacyCharge");
        if (historicalOnly && personId is Guid historicalPerson)
            charges = charges.Where(item => database.Ownerships.Any(ownership => ownership.ZevId == zevId
                && ownership.PersonId == historicalPerson && ownership.PropertyUnitId == item.PropertyUnitId
                && ownership.ValidFrom <= item.Period
                && (ownership.ValidTo == null || ownership.ValidTo > item.Period)));

        var payments = FinancialReadModel.ActivePayments(database, zevId, to).Where(item => ownerScope);
        if (historicalOnly && personId is Guid historicalPaymentPerson)
            payments = payments.Where(item => database.Ownerships.Any(ownership => ownership.ZevId == zevId
                && ownership.PersonId == historicalPaymentPerson && ownership.ValidFrom <= item.PaidOn
                && (ownership.ValidTo == null || ownership.ValidTo > item.PaidOn)));
        if (personId is Guid payerId && ownerScope)
        {
            var histories = database.PaymentIdentificationHistories.AsNoTracking();
            if (end is null) payments = payments.Where(item => item.PersonId == payerId);
            else payments = payments.Where(item =>
                histories.Where(history => history.PaymentId == item.Id && history.ChangedUtc < end)
                    .OrderByDescending(history => history.ChangedUtc).ThenByDescending(history => history.Id)
                    .Select(history => (Guid?)history.NewPersonId).FirstOrDefault() == payerId
                || !histories.Any(history => history.PaymentId == item.Id && history.ChangedUtc < end)
                    && (histories.Where(history => history.PaymentId == item.Id && history.ChangedUtc >= end)
                        .OrderBy(history => history.ChangedUtc).ThenBy(history => history.Id)
                        .Select(history => history.PreviousPersonId).FirstOrDefault() == payerId
                        || !histories.Any(history => history.PaymentId == item.Id)
                            && item.PersonId == payerId));
        }

        var allocations = from allocation in FinancialReadModel.ActiveAllocations(database, zevId, to)
            join charge in charges on allocation.ChargeId equals charge.Id
            select new FinancialAllocationFact
            {
                Id = allocation.Id, ChargeId = allocation.ChargeId, PaymentId = allocation.PaymentId,
                PersonId = charge.PersonId, PropertyUnitId = charge.PropertyUnitId, Period = charge.Period,
                CreatedUtc = allocation.CreatedUtc, Amount = allocation.Amount,
                CurrencyCode = charge.CurrencyCode, PaymentCurrencyCode = allocation.Payment.CurrencyCode
            };
        var chargeTotals = await charges.GroupBy(item => item.CurrencyCode)
            .Select(group => new FinancialCurrencyAmount(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var chargeAllocations = await allocations.GroupBy(item => item.CurrencyCode)
            .Select(group => new FinancialCurrencyAmount(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var paymentTotals = ownerScope
            ? await payments.GroupBy(item => item.CurrencyCode)
                .Select(group => new FinancialCurrencyAmount(group.Key, group.Sum(item => item.Amount)))
                .ToListAsync(cancellationToken)
            : chargeAllocations;
        var paymentAllocations = ownerScope
            ? await FinancialReadModel.ActiveAllocations(database, zevId, to)
                .Where(item => payments.Any(payment => payment.Id == item.PaymentId))
                .GroupBy(item => item.Payment.CurrencyCode)
                .Select(group => new FinancialCurrencyAmount(group.Key, group.Sum(item => item.Amount)))
                .ToListAsync(cancellationToken)
            : chargeAllocations;
        var balances = FinancialReadModel.GroupByCurrency(chargeTotals, paymentTotals,
            chargeAllocations, paymentAllocations);
        if (balancesOnly) return new(balances, page, pageSize, 0, []);

        var chargeEvents = charges.Select(item => new FinancialLedgerRow
        {
            Id = item.Id, CreatedUtc = item.CreatedUtc, Kind = item.Kind,
            Document = item.Document, Description = item.Description,
            CurrencyCode = item.CurrencyCode, Debt = item.Amount, Payment = 0m,
            CreditDelta = 0m, ChargeId = item.Id, PaymentId = null,
            PersonId = item.PersonId, PropertyUnitId = item.PropertyUnitId, Period = item.Period
        });
        var paymentEvents = payments.Select(item => new FinancialLedgerRow
        {
            Id = item.Id, CreatedUtc = item.CreatedUtc, Kind = "Payment",
            Document = item.Id.ToString(), Description = "Uplata",
            CurrencyCode = item.CurrencyCode, Debt = 0m, Payment = item.Amount,
            CreditDelta = item.Amount, ChargeId = null, PaymentId = item.Id,
            PersonId = item.PersonId, PropertyUnitId = null, Period = null
        });
        var allocationEvents = allocations.Select(item => new FinancialLedgerRow
        {
            Id = item.Id, CreatedUtc = item.CreatedUtc,
            Kind = item.Amount < 0m ? "AllocationRelease" : "Allocation",
            Document = item.Id.ToString(), Description = item.Amount < 0m
                ? "Ispravka raspoređivanja / oslobođen kredit" : "Raspoređeno",
            CurrencyCode = item.CurrencyCode, Debt = 0m,
            Payment = ownerScope ? 0m : item.Amount,
            CreditDelta = ownerScope ? -item.Amount : 0m,
            ChargeId = item.ChargeId, PaymentId = item.PaymentId,
            PersonId = item.PersonId, PropertyUnitId = item.PropertyUnitId, Period = item.Period
        });
        var eventsQuery = ownerScope
            ? chargeEvents.Concat(paymentEvents).Concat(allocationEvents)
            : chargeEvents.Concat(allocationEvents);
        var filteredEvents = start is null ? eventsQuery : eventsQuery.Where(item => item.CreatedUtc >= start);
        var totalCount = await filteredEvents.CountAsync(cancellationToken);
        var offset = (page - 1) * pageSize;
        var running = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (start is not null)
        {
            var opening = await eventsQuery.Where(item => item.CreatedUtc < start)
                .GroupBy(item => item.CurrencyCode)
                .Select(group => new FinancialCurrencyAmount(group.Key,
                    group.Sum(item => item.Debt - item.Payment)))
                .ToListAsync(cancellationToken);
            foreach (var item in opening) running[item.CurrencyCode] = item.Amount;
        }
        if (offset > 0)
        {
            var preceding = await filteredEvents.OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id)
                .Take(offset).GroupBy(item => item.CurrencyCode)
                .Select(group => new FinancialCurrencyAmount(group.Key,
                    group.Sum(item => item.Debt - item.Payment)))
                .ToListAsync(cancellationToken);
            foreach (var item in preceding)
                running[item.CurrencyCode] = running.GetValueOrDefault(item.CurrencyCode) + item.Amount;
        }
        var rows = await filteredEvents.OrderBy(item => item.CreatedUtc).ThenBy(item => item.Id)
            .Skip(offset).Take(pageSize).ToListAsync(cancellationToken);
        var events = rows.Select(item =>
        {
            var balance = running.GetValueOrDefault(item.CurrencyCode) + item.Debt - item.Payment;
            running[item.CurrencyCode] = balance;
            return new OwnerLedgerEvent(DateOnly.FromDateTime(item.CreatedUtc.UtcDateTime), item.Kind,
                item.Document, item.Description, item.CurrencyCode, item.Debt, item.Payment,
                item.CreditDelta, item.ChargeId, item.PaymentId)
            { RunningBalance = balance, PersonId = item.PersonId,
                PropertyUnitId = item.PropertyUnitId, Period = item.Period };
        }).ToList();
        return new(balances, page, pageSize, totalCount, events);
    }

    private sealed class FinancialAllocationFact
    {
        public Guid Id { get; init; }
        public Guid ChargeId { get; init; }
        public Guid PaymentId { get; init; }
        public Guid PersonId { get; init; }
        public Guid PropertyUnitId { get; init; }
        public DateOnly Period { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public decimal Amount { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public string PaymentCurrencyCode { get; init; } = string.Empty;
    }

    private sealed class FinancialLedgerRow
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Document { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string CurrencyCode { get; init; } = string.Empty;
        public decimal Debt { get; init; }
        public decimal Payment { get; init; }
        public decimal CreditDelta { get; init; }
        public Guid? ChargeId { get; init; }
        public Guid? PaymentId { get; init; }
        public Guid? PersonId { get; init; }
        public Guid? PropertyUnitId { get; init; }
        public DateOnly? Period { get; init; }
    }
}
