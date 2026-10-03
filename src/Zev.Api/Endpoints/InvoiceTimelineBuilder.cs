namespace Zev.Api.Endpoints;

public sealed record TimelineAllocation(Guid PaymentId, Guid? FinancialDocumentId, decimal Amount,
    DateOnly PaidOn, DateTimeOffset PaymentCreatedUtc, DateOnly? DocumentIssueDate,
    DateTimeOffset? DocumentCreatedUtc);

public static class InvoiceTimelineBuilder
{
    public static IReadOnlyList<DocumentTimelineEvent> PaymentEvents(IEnumerable<TimelineAllocation> allocations)
    {
        var rows = allocations.ToList();
        var payments = rows.Where(item => item.Amount > 0m)
            .GroupBy(item => item.PaymentId)
            .Select(group => new DocumentTimelineEvent(group.First().PaymentCreatedUtc, "Payment", null,
                group.Sum(item => item.Amount), group.Key, group.First().PaidOn));
        var releases = rows.Where(item => item.Amount < 0m && item.FinancialDocumentId is not null)
            .GroupBy(item => new { item.PaymentId, item.FinancialDocumentId })
            .Select(group => new DocumentTimelineEvent(group.First().DocumentCreatedUtc
                    ?? group.First().PaymentCreatedUtc, "AllocationRelease", null,
                -group.Sum(item => item.Amount), group.Key.PaymentId,
                group.First().DocumentIssueDate ?? group.First().PaidOn,
                FinancialDocumentId: group.Key.FinancialDocumentId));
        return payments.Concat(releases).ToList();
    }
}
