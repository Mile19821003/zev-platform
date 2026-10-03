using Xunit;
using Zev.Api.Endpoints;

namespace Zev.Api.Tests;

public sealed class InvoiceTimelineBuilderTests
{
    [Fact]
    public void PaymentAndReleaseAreEachGroupedByTheirRealEvent()
    {
        var paymentId = Guid.NewGuid();
        var stornoId = Guid.NewGuid();
        var paidOn = new DateOnly(2026, 9, 10);
        var releasedOn = new DateOnly(2026, 9, 15);
        var paidAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var releasedAt = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        TimelineAllocation Row(decimal amount, Guid? documentId = null) =>
            new(paymentId, documentId, amount, paidOn, paidAt,
                documentId is null ? null : releasedOn,
                documentId is null ? null : releasedAt);

        var events = InvoiceTimelineBuilder.PaymentEvents([
            Row(60m), Row(40m), Row(-60m, stornoId), Row(-40m, stornoId)
        ]);

        Assert.Collection(events,
            payment => { Assert.Equal("Payment", payment.Type); Assert.Equal(100m, payment.Amount);
                Assert.Equal(paymentId, payment.SourceId); },
            release => { Assert.Equal("AllocationRelease", release.Type); Assert.Equal(100m, release.Amount);
                Assert.Equal(releasedOn, release.Date); });
    }

    [Fact]
    public void SeparateCorrectionDocumentsKeepSeparateReleaseEvents()
    {
        var paymentId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var rows = new[] {
            new TimelineAllocation(paymentId, firstId, -10m, new DateOnly(2026, 9, 1), timestamp,
                new DateOnly(2026, 9, 2), timestamp),
            new TimelineAllocation(paymentId, secondId, -5m, new DateOnly(2026, 9, 1), timestamp,
                new DateOnly(2026, 9, 3), timestamp)
        };
        Assert.Equal(2, InvoiceTimelineBuilder.PaymentEvents(rows).Count);
    }
}
