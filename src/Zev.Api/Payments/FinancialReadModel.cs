using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Payments;

public sealed class FinancialChargeFact
{
    public Guid Id { get; init; }
    public Guid PersonId { get; init; }
    public Guid PropertyUnitId { get; init; }
    public Guid FundId { get; init; }
    public Guid? ReversesChargeId { get; init; }
    public Guid? InvoiceId { get; init; }
    public Guid? OriginalInvoiceId { get; init; }
    public DateOnly Period { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = FinancialReadModel.UnknownCurrency;
    public string Kind { get; init; } = string.Empty;
    public string Document { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record FinancialCurrencyAmount(string CurrencyCode, decimal Amount);
public sealed class FinancialFundMovement
{
    public Guid FundId { get; init; }
    public DateOnly EffectiveOn { get; init; }
    public string CurrencyCode { get; init; } = FinancialReadModel.UnknownCurrency;
    public decimal Amount { get; init; }
}
public sealed record InvoiceFinancialState(Guid InvoiceId, IReadOnlyList<OwnerCurrencyBalance> Balances,
    decimal PaidBeforeStorno, decimal StornoCredit);
public sealed class FinancialOpenItem
{
    public Guid Id { get; init; }
    public Guid PersonId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public Guid PropertyUnitId { get; init; }
    public Guid BuildingId { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public Guid FundId { get; init; }
    public DateOnly Period { get; init; }
    public DateOnly DueDate { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = FinancialReadModel.UnknownCurrency;
    public decimal Amount { get; init; }
    public decimal Correction { get; init; }
    public decimal Allocated { get; init; }
    public decimal Outstanding => FinancialReadModel.CalculateChargeOpen(
        FinancialReadModel.EffectiveInvoiceAmount(Amount, Correction), Allocated);
}

public static class FinancialReadModel
{
    public const string UnknownCurrency = "UNKNOWN";

    public static DateTimeOffset? EndOfDay(DateOnly? date) => date is DateOnly value
        ? new DateTimeOffset(value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
        : null;

    public static IQueryable<Payment> ActivePayments(ZevDbContext database, Guid zevId, DateOnly? to)
    {
        var end = EndOfDay(to);
        return database.Payments.AsNoTracking().Where(item => item.ZevId == zevId
            && (end == null || item.CreatedUtc < end)
            && (item.Status == PaymentStatus.Posted || to != null && item.ReversedOn > to));
    }

    public static IQueryable<PaymentAllocation> ActiveAllocations(ZevDbContext database, Guid zevId,
        DateOnly? to)
    {
        var end = EndOfDay(to);
        return database.PaymentAllocations.AsNoTracking().Where(item => item.ZevId == zevId
            && (end == null || item.CreatedUtc < end && item.Charge.CreatedUtc < end)
            && (item.Payment.Status == PaymentStatus.Posted || to != null && item.Payment.ReversedOn > to));
    }

    public static IQueryable<FinancialChargeFact> Charges(ZevDbContext database, Guid zevId, DateOnly? to)
    {
        var end = EndOfDay(to);
        return database.Charges.AsNoTracking().Where(item => item.ZevId == zevId
            && (end == null || item.CreatedUtc < end))
            .Select(item => new FinancialChargeFact
            {
                Id = item.Id, PersonId = item.PersonId, PropertyUnitId = item.PropertyUnitId,
                ReversesChargeId = item.ReversesChargeId,
                FundId = item.FundId, Period = item.Period, DueDate = item.DueDate,
                CreatedUtc = item.CreatedUtc, Amount = item.Amount,
                InvoiceId = database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                    .Select(line => (Guid?)line.InvoiceId).FirstOrDefault(),
                OriginalInvoiceId = database.FinancialDocuments.Where(document => document.Id == item.FinancialDocumentId)
                    .Select(document => (Guid?)document.OriginalInvoiceId).FirstOrDefault(),
                CurrencyCode = database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                    .Select(line => line.Invoice.Currency).FirstOrDefault()
                    ?? database.FinancialDocumentLines.Where(line => line.ChargeId == item.Id)
                        .Select(line => line.Document.Currency).FirstOrDefault()
                    ?? (database.PaymentAllocations.Where(allocation => allocation.ChargeId == item.Id
                            && (end == null || allocation.CreatedUtc < end
                                && allocation.Payment.CreatedUtc < end))
                            .Select(allocation => allocation.Payment.CurrencyCode).Distinct().Count() == 1
                        ? database.PaymentAllocations.Where(allocation => allocation.ChargeId == item.Id
                                && (end == null || allocation.CreatedUtc < end
                                    && allocation.Payment.CreatedUtc < end))
                            .Select(allocation => allocation.Payment.CurrencyCode).FirstOrDefault()
                        : UnknownCurrency) ?? UnknownCurrency,
                Kind = database.FinancialDocuments.Any(document => document.Id == item.FinancialDocumentId
                        && document.Type == FinancialDocumentType.Storno) ? "Storno"
                    : item.FinancialDocumentId != null ? "Correction"
                    : database.InvoiceLines.Any(line => line.ChargeId == item.Id) ? "Invoice" : "LegacyCharge",
                Document = database.FinancialDocuments.Where(document => document.Id == item.FinancialDocumentId)
                    .Select(document => document.DocumentNumber).FirstOrDefault()
                    ?? database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                        .Select(line => line.Invoice.InvoiceNumber).FirstOrDefault() ?? item.Reference,
                Description = database.FinancialDocumentLines.Where(line => line.ChargeId == item.Id)
                    .Select(line => line.DescriptionSnapshot).FirstOrDefault()
                    ?? database.InvoiceLines.Where(line => line.ChargeId == item.Id)
                        .Select(line => line.DescriptionSnapshot).FirstOrDefault() ?? "Zaduženje"
            });
    }

    public static IReadOnlyList<OwnerCurrencyBalance> GroupByCurrency(
        IReadOnlyList<FinancialCurrencyAmount> charges, IReadOnlyList<FinancialCurrencyAmount> payments,
        IReadOnlyList<FinancialCurrencyAmount> chargeAllocations,
        IReadOnlyList<FinancialCurrencyAmount> paymentAllocations)
    {
        var currencies = charges.Select(item => item.CurrencyCode)
            .Concat(payments.Select(item => item.CurrencyCode))
            .Concat(chargeAllocations.Select(item => item.CurrencyCode))
            .Concat(paymentAllocations.Select(item => item.CurrencyCode))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).ToList();
        return currencies.Select(currency =>
        {
            var charged = charges.Where(item => string.Equals(item.CurrencyCode, currency,
                StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
            var paid = payments.Where(item => string.Equals(item.CurrencyCode, currency,
                StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
            var allocatedToCharges = chargeAllocations.Where(item => string.Equals(item.CurrencyCode, currency,
                StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
            var allocatedFromPayments = paymentAllocations.Where(item => string.Equals(item.CurrencyCode, currency,
                StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
            var open = CalculateChargeOpen(charged, allocatedToCharges);
            var credit = CalculatePaymentCredit(paid, allocatedFromPayments);
            return new OwnerCurrencyBalance(currency, charged, paid, open, credit, open - credit);
        }).ToList();
    }

    public static decimal CalculateChargeOpen(decimal charges, decimal allocations) => charges - allocations;

    public static decimal CalculatePaymentCredit(decimal payments, decimal allocations) => payments - allocations;

    public static decimal EffectiveInvoiceAmount(decimal originalAmount, decimal corrections) =>
        originalAmount + corrections;

    public static IQueryable<FinancialFundMovement> FundMovements(ZevDbContext database, Guid zevId)
    {
        return database.FundTransactions.AsNoTracking().Where(item => item.ZevId == zevId)
            .Select(item => new FinancialFundMovement
            {
                FundId = item.FundId, EffectiveOn = item.EffectiveOn,
                CurrencyCode = item.Kind == FundTransactionKind.Payment
                    ? database.Payments.Where(payment => payment.ZevId == zevId
                            && payment.Id == item.SourceId)
                        .Select(payment => payment.CurrencyCode).FirstOrDefault() ?? UnknownCurrency
                    : item.Kind == FundTransactionKind.Reversal && item.ReversesTransaction != null
                        && item.ReversesTransaction.Kind == FundTransactionKind.Payment
                        ? database.Payments.Where(payment => payment.ZevId == zevId
                                && payment.Id == item.ReversesTransaction.SourceId)
                            .Select(payment => payment.CurrencyCode).FirstOrDefault() ?? UnknownCurrency
                        : UnknownCurrency,
                Amount = item.SignedAmount
            });
    }

    public static async Task<IReadOnlyList<OwnerCurrencyBalance>> ChargeIdsAsync(ZevDbContext database,
        Guid zevId, IReadOnlyCollection<Guid> chargeIds, DateOnly? to, CancellationToken cancellationToken)
    {
        if (chargeIds.Count == 0) return [];
        var ids = chargeIds.ToArray();
        var charges = Charges(database, zevId, to).Where(item => ids.Contains(item.Id));
        var charged = await charges.GroupBy(item => item.CurrencyCode)
            .Select(group => new FinancialCurrencyAmount(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var allocations = await (from allocation in ActiveAllocations(database, zevId, to)
            join charge in charges on allocation.ChargeId equals charge.Id
            group allocation by charge.CurrencyCode into grouped
            select new FinancialCurrencyAmount(grouped.Key, grouped.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        return GroupByCurrency(charged, allocations, allocations, allocations);
    }

    public static IQueryable<FinancialOpenItem> OpenItems(ZevDbContext database, Guid zevId,
        DateOnly asOf)
    {
        var end = EndOfDay(asOf);
        var allocations = ActiveAllocations(database, zevId, asOf);
        return from fact in Charges(database, zevId, asOf)
            join charge in database.Charges.AsNoTracking() on fact.Id equals charge.Id
            where fact.Amount > 0m && fact.Period <= asOf
            select new FinancialOpenItem
            {
                Id = fact.Id, PersonId = fact.PersonId, PersonName = charge.Person.DisplayName,
                PropertyUnitId = fact.PropertyUnitId,
                BuildingId = charge.PropertyUnit.BuildingId, UnitCode = charge.PropertyUnit.Code,
                FundId = fact.FundId,
                Period = fact.Period, DueDate = fact.DueDate, Reference = charge.Reference,
                CurrencyCode = fact.CurrencyCode, Amount = fact.Amount,
                Correction = database.Charges.Where(correction => correction.ReversesChargeId == fact.Id
                        && correction.CreatedUtc < end)
                    .Sum(correction => (decimal?)correction.Amount) ?? 0m,
                Allocated = allocations.Where(allocation => allocation.ChargeId == fact.Id)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            };
    }

    public static async Task<IReadOnlyDictionary<Guid, InvoiceFinancialState>> InvoiceStatesAsync(
        ZevDbContext database, Guid zevId, IReadOnlyCollection<Guid> invoiceIds, DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0) return new Dictionary<Guid, InvoiceFinancialState>();
        var ids = invoiceIds.ToArray();
        var charges = Charges(database, zevId, to).Where(item =>
            item.InvoiceId != null && ids.Contains(item.InvoiceId.Value)
            || item.OriginalInvoiceId != null && ids.Contains(item.OriginalInvoiceId.Value));
        var charged = await charges.GroupBy(item => new
            { InvoiceId = item.InvoiceId ?? item.OriginalInvoiceId, item.CurrencyCode })
            .Select(group => new { group.Key.InvoiceId, group.Key.CurrencyCode,
                Amount = group.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
        var paid = await (from allocation in ActiveAllocations(database, zevId, to)
                join charge in charges on allocation.ChargeId equals charge.Id
                group allocation by new { InvoiceId = charge.InvoiceId ?? charge.OriginalInvoiceId,
                    charge.CurrencyCode } into grouped
                select new { grouped.Key.InvoiceId, grouped.Key.CurrencyCode,
                    Amount = grouped.Sum(item => item.Amount) }).ToListAsync(cancellationToken);
        var originalPaid = await (from allocation in ActiveAllocations(database, zevId, to)
                join line in database.InvoiceLines.AsNoTracking() on allocation.ChargeId equals line.ChargeId
                where ids.Contains(line.InvoiceId) && allocation.Amount > 0m
                group allocation by line.InvoiceId into grouped
                select new { InvoiceId = grouped.Key, Amount = grouped.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.InvoiceId, item => item.Amount, cancellationToken);
        var stornoReleases = await (from allocation in ActiveAllocations(database, zevId, to)
                join document in database.FinancialDocuments.AsNoTracking()
                    on allocation.FinancialDocumentId equals (Guid?)document.Id
                where ids.Contains(document.OriginalInvoiceId) && document.Type == FinancialDocumentType.Storno
                    && allocation.Amount < 0m
                group allocation by document.OriginalInvoiceId into grouped
                select new { InvoiceId = grouped.Key, Amount = -grouped.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.InvoiceId, item => item.Amount, cancellationToken);
        return ids.ToDictionary(invoiceId => invoiceId, invoiceId =>
        {
            var chargeTotals = charged.Where(item => item.InvoiceId == invoiceId)
                .Select(item => new FinancialCurrencyAmount(item.CurrencyCode, item.Amount)).ToList();
            var allocated = paid.Where(item => item.InvoiceId == invoiceId)
                .Select(item => new FinancialCurrencyAmount(item.CurrencyCode, item.Amount)).ToList();
            return new InvoiceFinancialState(invoiceId,
                GroupByCurrency(chargeTotals, allocated, allocated, allocated),
                originalPaid.GetValueOrDefault(invoiceId), stornoReleases.GetValueOrDefault(invoiceId));
        });
    }
}
