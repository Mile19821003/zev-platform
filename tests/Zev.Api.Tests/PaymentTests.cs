using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;
using Zev.Api.Security;

namespace Zev.Api.Tests;

public sealed class PaymentTests
{
    [Fact]
    public void DerivedPaymentStatusDistinguishesRemainingCreditFromFullAllocation()
    {
        var personId = Guid.NewGuid();
        Assert.Equal("PARTIALLY_ALLOCATED_CREDIT_REMAINS", PaymentAllocationStatus.Derive(120m, 100m,
            personId, PaymentStatus.Posted, BankTransactionStatus.Matched));
        Assert.Equal("IDENTIFIED_UNALLOCATED", PaymentAllocationStatus.Derive(100m, 0m,
            personId, PaymentStatus.Posted, BankTransactionStatus.Matched));
        Assert.Equal("FULLY_ALLOCATED", PaymentAllocationStatus.Derive(100m, 100m,
            personId, PaymentStatus.Posted, BankTransactionStatus.Matched));
        Assert.Equal("MANUAL_REVIEW", PaymentAllocationStatus.Derive(100m, 0m,
            null, PaymentStatus.Posted, BankTransactionStatus.ManualReview));
    }
    [Fact]
    public async Task ChangePayerReversesAllocationAtomicallyAndMovesCredit()
    {
        var actorId = Guid.NewGuid();
        using var fixture = new PaymentFixture(2, actorId: actorId);
        var original = fixture.Charges[0];
        var replacement = fixture.Charges[1];
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), original.PersonId, null, original.FundId,
                new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Test", null, null,
                [new PaymentAllocationInput(original.Id, 100m)]), actorId, CancellationToken.None);
        var paymentId = Assert.IsType<Guid>(created.PaymentId);
        var changed = await workflow.ChangePayerAsync(fixture.Zev.Id, paymentId, replacement.PersonId,
            "Wrong payer", null, actorId, CancellationToken.None);
        Assert.True(changed.Success, changed.Error);
        Assert.Equal(replacement.PersonId, fixture.Database.Payments.Single().PersonId);
        Assert.Equal([100m, -100m], fixture.Database.PaymentAllocations
            .OrderBy(item => item.Amount < 0m).Select(item => item.Amount).ToArray());
        var history = Assert.Single(fixture.Database.PaymentIdentificationHistories);
        Assert.Equal(original.PersonId, history.PreviousPersonId);
        Assert.Equal(replacement.PersonId, history.NewPersonId);
        Assert.Equal(actorId, history.ChangedByUserId);
        Assert.Equal("Wrong payer", history.Reason);
        var card = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var oldOwner = await card.ReadAsync(fixture.Zev.Id, original.PersonId, null, null, 1, 20,
            CancellationToken.None);
        var newOwner = await card.ReadAsync(fixture.Zev.Id, replacement.PersonId, null, null, 1, 20,
            CancellationToken.None);
        Assert.Equal(0m, Assert.Single(oldOwner.Balances).Credit);
        Assert.Equal(100m, newOwner.Balances.Single(item => item.CurrencyCode == "BAM").Credit);
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(PaymentIdentificationHistory)
            && item.ActorUserId == actorId && item.CorrelationId is not null);
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(Payment)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default);
        Assert.Equal(2, fixture.Database.AuditLogs.Count(item => item.EntityType == nameof(PaymentAllocation)
            && item.Action == "Added" && item.ActorUserId == actorId));
        var allocated = await workflow.AllocateAsync(fixture.Zev.Id, paymentId, Guid.NewGuid(),
            [new PaymentAllocationInput(replacement.Id, 100m)], CancellationToken.None, actorId);
        Assert.True(allocated.Success, allocated.Error);
        Assert.Single(fixture.Database.Payments);
        Assert.Equal(100m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.False((await workflow.ChangePayerAsync(fixture.Zev.Id, paymentId, original.PersonId,
            "", null, actorId, CancellationToken.None)).Success);
    }

    [Fact]
    public async Task InitialPayerIdentificationHasActorAndDedicatedHistory()
    {
        var actorId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, actorId: actorId);
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "unrecognized.csv", "CSV", Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-01,25.00,UNKNOWN"),
            CancellationToken.None);
        var payment = Assert.Single(fixture.Database.Payments);
        var result = await new PaymentWorkflowService(fixture.Database).IdentifyAsync(fixture.Zev.Id,
            payment.Id, fixture.Charges[0].PersonId, CancellationToken.None, actorId);
        Assert.True(result.Success, result.Error);
        var history = Assert.Single(fixture.Database.PaymentIdentificationHistories);
        Assert.Null(history.PreviousPersonId);
        Assert.Equal(actorId, history.ChangedByUserId);
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(PaymentIdentificationHistory)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default);
    }

    [Fact]
    public async Task ManualPaymentAllocationAndReleaseHaveAuditableActorAndReason()
    {
        var actorId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, actorId: actorId);
        var charge = fixture.Charges[0];
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), charge.PersonId, null, charge.FundId,
                new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Opening payment",
                null, null, null), actorId, CancellationToken.None);
        Assert.True(created.Success, created.Error);
        var allocated = await workflow.AllocateAsync(fixture.Zev.Id, created.PaymentId!.Value,
            Guid.NewGuid(), [new PaymentAllocationInput(charge.Id, 100m)], CancellationToken.None, actorId);
        Assert.True(allocated.Success, allocated.Error);
        var original = Assert.Single(fixture.Database.PaymentAllocations);
        var released = await workflow.ReverseAllocationAsync(fixture.Zev.Id, original.Id,
            "Incorrect allocation", CancellationToken.None);
        Assert.True(released.Success, released.Error);
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(Payment)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default
            && item.CorrelationId is not null && item.AfterJson!.Contains("Opening payment"));
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(PaymentAllocation)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default
            && item.CorrelationId is not null && item.AfterJson!.Contains(original.Id.ToString()));
        Assert.Contains(fixture.Database.AuditLogs, item => item.EntityType == nameof(PaymentAllocation)
            && item.Action == "Added" && item.ActorUserId == actorId && item.OccurredUtc != default
            && item.CorrelationId is not null && item.AfterJson!.Contains("Incorrect allocation"));
    }

    [Fact]
    public async Task InvalidReplacementAllocationRollsBackEntirePayerCorrection()
    {
        using var fixture = new PaymentFixture(2);
        var original = fixture.Charges[0];
        var replacement = fixture.Charges[1];
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), original.PersonId, null, original.FundId,
                new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Test", null, null,
                [new PaymentAllocationInput(original.Id, 100m)]), null, CancellationToken.None);
        var failed = await workflow.ChangePayerAsync(fixture.Zev.Id, created.PaymentId!.Value,
            replacement.PersonId, "Wrong payer", [new PaymentAllocationInput(original.Id, 100m)],
            null, CancellationToken.None);
        Assert.False(failed.Success);
        Assert.Equal(original.PersonId, fixture.Database.Payments.Single().PersonId);
        Assert.Single(fixture.Database.PaymentAllocations);
        Assert.Empty(fixture.Database.PaymentIdentificationHistories);
        Assert.DoesNotContain(fixture.Database.ChangeTracker.Entries<PaymentAllocation>(),
            item => item.State == EntityState.Added);
    }

    [Fact]
    public async Task ExactThreeWayAllocationPreservesOpenAndCredit()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 30m);
        var first = fixture.Charges[0];
        var second = new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Period = first.Period, DueDate = first.DueDate, Amount = 40m, Reference = "B" };
        var third = new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Period = first.Period, DueDate = first.DueDate, Amount = 50m, Reference = "C" };
        fixture.Database.Charges.AddRange(second, third);
        await fixture.Database.SaveChangesAsync();
        var workflow = new PaymentWorkflowService(fixture.Database);
        var payment = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), first.PersonId, null, first.FundId,
                new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Test", null, null, null),
            null, CancellationToken.None);
        Assert.True((await workflow.AllocateAsync(fixture.Zev.Id, payment.PaymentId!.Value, Guid.NewGuid(),
            [new(first.Id, 30m), new(second.Id, 40m), new(third.Id, 30m)], CancellationToken.None)).Success);
        Assert.Equal(0m, first.Amount - fixture.Database.PaymentAllocations.Where(item => item.ChargeId == first.Id)
            .Sum(item => item.Amount));
        Assert.Equal(0m, second.Amount - fixture.Database.PaymentAllocations.Where(item => item.ChargeId == second.Id)
            .Sum(item => item.Amount));
        Assert.Equal(20m, third.Amount - fixture.Database.PaymentAllocations.Where(item => item.ChargeId == third.Id)
            .Sum(item => item.Amount));
        Assert.Equal(0m, fixture.Database.Payments.Single().Amount
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
    }

    [Fact]
    public async Task OwnerSummaryIncludesMoreThanOneThousandChargesAndPaginatesEvents()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 1m);
        var first = fixture.Charges[0];
        fixture.Database.Charges.AddRange(Enumerable.Range(0, 1000).Select(index => new Charge
        {
            OrganizationId = first.OrganizationId, ZevId = first.ZevId, PersonId = first.PersonId,
            PropertyUnitId = first.PropertyUnitId, FundId = first.FundId, Amount = 1m,
            Period = first.Period, DueDate = first.DueDate, Reference = $"BULK-{index}"
        }));
        await fixture.Database.SaveChangesAsync();
        var card = await new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build())
            .ReadAsync(fixture.Zev.Id, first.PersonId, null, null, 2, 20, CancellationToken.None);
        Assert.Equal(1001m, Assert.Single(card.Balances).TotalCharges);
        Assert.Equal(1001, card.TotalCount);
        Assert.Equal(20, card.Events.Count);
        Assert.Equal(2, card.Page);
    }

    [Fact]
    public async Task OwnerLedgerRunningBalanceIncludesEventsBeforeFromAndPreviousPage()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 0m);
        var first = fixture.Charges[0];
        fixture.Database.Charges.AddRange(new Charge { OrganizationId = first.OrganizationId,
            ZevId = first.ZevId, PersonId = first.PersonId,
            PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Amount = 100m, Reference = "AUGUST", Period = new DateOnly(2026, 8, 1),
            CreatedUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) },
            new Charge { OrganizationId = first.OrganizationId,
            ZevId = first.ZevId, PersonId = first.PersonId,
            PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Amount = 10m, Reference = "SEPTEMBER", Period = new DateOnly(2026, 9, 1),
            CreatedUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) });
        fixture.Database.Payments.AddRange(
            new Payment { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                PersonId = first.PersonId, FundId = first.FundId, Amount = 40m,
                CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 8, 10),
                SourceType = PaymentSourceType.Manual, RequestId = Guid.NewGuid(),
                ManualMethod = "Migration", ManualReason = "Opening",
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) },
            new Payment { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                PersonId = first.PersonId, FundId = first.FundId, Amount = 5m,
                CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 9, 2),
                SourceType = PaymentSourceType.Manual, RequestId = Guid.NewGuid(),
                ManualMethod = "Migration", ManualReason = "Later",
                CreatedUtc = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero) });
        await fixture.Database.SaveChangesAsync();
        var augustCharge = fixture.Database.Charges.Single(item => item.Reference == "AUGUST");
        var septemberCharge = fixture.Database.Charges.Single(item => item.Reference == "SEPTEMBER");
        var augustPayment = fixture.Database.Payments.Single(item => item.Amount == 40m);
        var septemberPayment = fixture.Database.Payments.Single(item => item.Amount == 5m);
        fixture.Database.PaymentAllocations.AddRange(
            new PaymentAllocation { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                ChargeId = augustCharge.Id, PaymentId = augustPayment.Id, Amount = 40m,
                CreatedUtc = augustPayment.CreatedUtc },
            new PaymentAllocation { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                ChargeId = septemberCharge.Id, PaymentId = septemberPayment.Id, Amount = 5m,
                CreatedUtc = septemberPayment.CreatedUtc });
        await fixture.Database.SaveChangesAsync();
        var service = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var firstPage = await service.ReadAsync(fixture.Zev.Id, first.PersonId,
            new DateOnly(2026, 9, 1), null, 1, 1, CancellationToken.None);
        var thirdPage = await service.ReadAsync(fixture.Zev.Id, first.PersonId,
            new DateOnly(2026, 9, 1), null, 3, 1, CancellationToken.None);
        Assert.Equal(70m, Assert.Single(firstPage.Events).RunningBalance);
        Assert.Equal(65m, Assert.Single(thirdPage.Events).RunningBalance);
        Assert.Equal(65m, firstPage.Balances.Single(item => item.CurrencyCode == "BAM").NetBalance);
    }

    [Fact]
    public async Task OwnerSummaryNeverCombinesBamAndEur()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 100m);
        var original = fixture.Charges[0];
        var eurFund = new Fund { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            Name = "Euro fund", Kind = FundKind.Reserve };
        var eurCharge = new Charge { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PersonId = original.PersonId, PropertyUnitId = original.PropertyUnitId,
            Fund = eurFund, Amount = 50m, Reference = "EUR-CHARGE" };
        var bamPayment = new Payment { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PersonId = original.PersonId, FundId = original.FundId, SourceType = PaymentSourceType.Manual,
            RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Test",
            CurrencyCode = "BAM", Amount = 80m, PaidOn = new DateOnly(2026, 9, 1) };
        var eurPayment = new Payment { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PersonId = original.PersonId, FundId = eurFund.Id,
            SourceType = PaymentSourceType.Manual, RequestId = Guid.NewGuid(),
            ManualMethod = "Migration", ManualReason = "Test", CurrencyCode = "EUR",
            Amount = 50m, PaidOn = new DateOnly(2026, 9, 1) };
        fixture.Database.AddRange(eurFund, eurCharge,
            new BankAccount { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
                Fund = eurFund, AccountNumber = "EUR-ACCOUNT", CurrencyCode = "EUR" },
            bamPayment, eurPayment,
            new PaymentAllocation { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
                Payment = bamPayment, Charge = original, Amount = 80m },
            new PaymentAllocation { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
                Payment = eurPayment, Charge = eurCharge, Amount = 50m });
        await fixture.Database.SaveChangesAsync();
        var card = await new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build())
            .ReadAsync(fixture.Zev.Id, original.PersonId, null, null, 1, 20, CancellationToken.None);
        Assert.Equal(2, card.Balances.Count);
        var bam = card.Balances.Single(item => item.CurrencyCode == "BAM");
        var eur = card.Balances.Single(item => item.CurrencyCode == "EUR");
        Assert.Equal((100m, 80m, 20m), (bam.TotalCharges, bam.TotalPayments, bam.Open));
        Assert.Equal((50m, 50m, 0m), (eur.TotalCharges, eur.TotalPayments, eur.Open));
    }

    [Fact]
    public async Task AutomaticCandidateLimitCountsOnlyOpenCharges()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 30m);
        var first = fixture.Charges[0];
        var closedCharges = Enumerable.Range(0, 5).Select(index => new Charge
        {
            OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 30),
            Amount = 1m, Reference = $"CLOSED-{index}"
        }).ToArray();
        var second = new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
            Amount = 10m, Reference = "SECOND-OPEN" };
        var oldPayment = new Payment { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, FundId = first.FundId, SourceType = PaymentSourceType.Manual,
            RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Closed",
            Amount = 5m, CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 8, 20) };
        fixture.Database.AddRange(closedCharges);
        fixture.Database.AddRange(second, oldPayment);
        fixture.Database.PaymentAllocations.AddRange(closedCharges.Select(charge => new PaymentAllocation
        {
            OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            Payment = oldPayment, Charge = charge, Amount = 1m
        }));
        await fixture.Database.SaveChangesAsync();
        var workflow = new PaymentWorkflowService(fixture.Database);
        var credit = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), first.PersonId, null, first.FundId,
                new DateOnly(2026, 9, 1), 40m, "BAM", "Migration", "Credit", null, null, null),
            null, CancellationToken.None);
        var preview = await workflow.PreviewAutomaticAsync(fixture.Zev.Id, credit.PaymentId!.Value,
            null, CancellationToken.None, maxCharges: 2);
        Assert.Null(preview.Error);
        Assert.Equal(2, preview.Lines.Count);
        Assert.Equal(40m, preview.Allocated);
    }

    [Fact]
    public async Task HistoricalCardExcludesLaterAllocationRelease()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 0m);
        var original = fixture.Charges[0];
        var charge = new Charge { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PersonId = original.PersonId, PropertyUnitId = original.PropertyUnitId, FundId = original.FundId,
            Amount = 100m, Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 31),
            Reference = "HIST", CreatedUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) };
        var payment = new Payment { OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PersonId = original.PersonId, FundId = original.FundId, SourceType = PaymentSourceType.Manual,
            RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "History",
            CurrencyCode = "BAM", Amount = 100m, PaidOn = new DateOnly(2026, 8, 10),
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
        var allocation = new PaymentAllocation { OrganizationId = original.OrganizationId,
            ZevId = original.ZevId, Charge = charge, Payment = payment, Amount = 100m,
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero) };
        fixture.Database.AddRange(charge, payment, allocation);
        await fixture.Database.SaveChangesAsync();
        fixture.Database.PaymentAllocations.Add(new PaymentAllocation
        {
            OrganizationId = original.OrganizationId, ZevId = original.ZevId,
            PaymentId = payment.Id, ChargeId = charge.Id, Amount = -100m,
            ReversesAllocationId = allocation.Id, ReversalReason = "Correction",
            CreatedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
        });
        await fixture.Database.SaveChangesAsync();
        var service = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var august = await service.ReadAsync(fixture.Zev.Id, original.PersonId, null,
            new DateOnly(2026, 8, 31), 1, 20, CancellationToken.None);
        var september = await service.ReadAsync(fixture.Zev.Id, original.PersonId, null,
            new DateOnly(2026, 9, 6), 1, 20, CancellationToken.None);
        Assert.Equal(0m, Assert.Single(august.Balances).Open);
        Assert.Equal(0m, Assert.Single(august.Balances).Credit);
        Assert.Equal(100m, Assert.Single(september.Balances).Open);
        Assert.Equal(100m, Assert.Single(september.Balances).Credit);
    }

    [Fact]
    public async Task HistoricalCardAttributesPaymentToPayerBeforeCorrection()
    {
        using var fixture = new PaymentFixture(2);
        var oldPersonId = fixture.Charges[0].PersonId;
        var newPersonId = fixture.Charges[1].PersonId;
        var payment = new Payment { OrganizationId = fixture.Charges[0].OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Charges[0].FundId,
            PersonId = newPersonId, SourceType = PaymentSourceType.Manual,
            RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "Historic payer",
            CurrencyCode = "BAM", Amount = 100m, PaidOn = new DateOnly(2026, 8, 10),
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
        fixture.Database.Payments.Add(payment);
        fixture.Database.PaymentIdentificationHistories.Add(new PaymentIdentificationHistory
        {
            OrganizationId = payment.OrganizationId, ZevId = payment.ZevId, Payment = payment,
            PreviousPersonId = oldPersonId, NewPersonId = newPersonId, Reason = "Corrected payer",
            ChangedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
        });
        await fixture.Database.SaveChangesAsync();
        var service = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var beforeOld = await service.ReadAsync(fixture.Zev.Id, oldPersonId, null,
            new DateOnly(2026, 8, 31), 1, 20, CancellationToken.None);
        var beforeNew = await service.ReadAsync(fixture.Zev.Id, newPersonId, null,
            new DateOnly(2026, 8, 31), 1, 20, CancellationToken.None);
        var afterNew = await service.ReadAsync(fixture.Zev.Id, newPersonId, null,
            new DateOnly(2026, 9, 6), 1, 20, CancellationToken.None);
        Assert.Equal(100m, Assert.Single(beforeOld.Balances).TotalPayments);
        Assert.Empty(beforeNew.Balances);
        Assert.Equal(100m, Assert.Single(afterNew.Balances).TotalPayments);
    }
    [Fact]
    public async Task ManualPaymentCanRemainCreditAndRequestIsIdempotent()
    {
        using var fixture = new PaymentFixture(1);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:AllowManualCash"] = "true"
        }).Build();
        var workflow = new PaymentWorkflowService(fixture.Database, configuration);
        var source = fixture.Charges[0];
        var request = new ManualPaymentInput(Guid.NewGuid(), source.PersonId, null, source.FundId,
            new DateOnly(2026, 9, 1), 120m, "BAM", "Cash", "UAT gotovina", null, null, null);
        var first = await workflow.CreateManualAsync(fixture.Zev.Id, request, Guid.NewGuid(), CancellationToken.None);
        var retry = await workflow.CreateManualAsync(fixture.Zev.Id, request, null, CancellationToken.None);
        Assert.True(first.Success, first.Error);
        Assert.True(retry.AlreadyExists);
        Assert.Single(fixture.Database.Payments);
        var payment = Assert.Single(fixture.Database.Payments);
        Assert.Equal(PaymentSourceType.Manual, payment.SourceType);
        Assert.Null(payment.BankTransactionId);
        Assert.Equal(120m, fixture.Database.FundTransactions.Sum(item => item.SignedAmount));
        Assert.Empty(fixture.Database.PaymentAllocations);
    }

    [Fact]
    public async Task ManualCashRequiresExplicitConfiguration()
    {
        using var fixture = new PaymentFixture(1);
        var charge = fixture.Charges[0];
        var result = await new PaymentWorkflowService(fixture.Database).CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), charge.PersonId, null, charge.FundId,
                new DateOnly(2026, 9, 1), 10m, "BAM", "Cash", "Test", null, null, null),
            null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Empty(fixture.Database.Payments);
    }

    [Fact]
    public async Task ManualReconciliationRefreshesStatementStatus()
    {
        using var fixture = new PaymentFixture(1);
        var charge = fixture.Charges[0];
        var imported = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "unknown.csv", "CSV", Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-01,100.00,UNKNOWN"),
            CancellationToken.None);
        Assert.Equal(BankImportStatus.Imported, imported.Import?.Status);
        var payment = Assert.Single(fixture.Database.Payments);
        var result = await new PaymentWorkflowService(fixture.Database).AllocateAsync(fixture.Zev.Id,
            payment.Id, Guid.NewGuid(), [new(charge.Id, 100m)], CancellationToken.None);
        Assert.True(result.Success, result.Error);
        Assert.Equal(BankImportStatus.Reconciled, imported.Import?.Status);
        Assert.Equal(BankTransactionStatus.Matched, Assert.Single(fixture.Database.BankTransactions).Status);
    }

    [Fact]
    public async Task CreditSplitsAcrossChargesAndReversalPreservesHistory()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 30m);
        var first = fixture.Charges[0];
        var second = new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId, FundId = first.FundId,
            Amount = 50m, Period = first.Period, DueDate = first.DueDate,
            Reference = $"ZEV-{Guid.NewGuid():N}" };
        fixture.Database.Charges.Add(second);
        await fixture.Database.SaveChangesAsync();
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), first.PersonId, null, first.FundId,
                new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Početno stanje", null, null, null),
            null, CancellationToken.None);
        var paymentId = Assert.IsType<Guid>(created.PaymentId);
        var operationId = Guid.NewGuid();
        var lines = new[] { new PaymentAllocationInput(first.Id, 30m),
            new PaymentAllocationInput(second.Id, 40m) };
        var allocation = await workflow.AllocateAsync(fixture.Zev.Id, paymentId, operationId, lines,
            CancellationToken.None);
        var duplicate = await workflow.AllocateAsync(fixture.Zev.Id, paymentId, operationId, lines,
            CancellationToken.None);
        Assert.True(allocation.Success, allocation.Error);
        Assert.True(duplicate.AlreadyExists);
        Assert.Equal(70m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        var tooMuch = await workflow.AllocateAsync(fixture.Zev.Id, paymentId, Guid.NewGuid(),
            [new(second.Id, 20m)], CancellationToken.None);
        Assert.False(tooMuch.Success);
        var original = fixture.Database.PaymentAllocations.Single(item => item.ChargeId == first.Id);
        var reversed = await workflow.ReverseAllocationAsync(fixture.Zev.Id, original.Id,
            "Pogrešna raspodjela", CancellationToken.None);
        Assert.True(reversed.Success, reversed.Error);
        Assert.Equal(2, fixture.Database.PaymentAllocations.Count(item => item.ChargeId == first.Id));
        Assert.Equal(40m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        var preview = await workflow.PreviewAutomaticAsync(fixture.Zev.Id, paymentId, null,
            CancellationToken.None);
        Assert.Equal(60m, preview.Available);
        Assert.Contains(preview.Lines, item => item.ChargeId == first.Id && item.Amount == 30m);
    }

    [Fact]
    public async Task AutomaticCreditAllocationUsesExistingPaymentAndOldestCharge()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 50m);
        var charge = fixture.Charges[0];
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), charge.PersonId, null, charge.FundId,
                new DateOnly(2026, 9, 1), 20m, "BAM", "Migration", "Početni kredit", null, null, null),
            null, CancellationToken.None);
        var paymentId = Assert.IsType<Guid>(created.PaymentId);
        var preview = await workflow.PreviewAutomaticAsync(fixture.Zev.Id, paymentId, null,
            CancellationToken.None);
        Assert.Equal(20m, preview.Available);
        Assert.Equal(20m, preview.Allocated);
        Assert.Equal(charge.Id, Assert.Single(preview.Lines).ChargeId);
        var error = await workflow.ApplyAvailableCreditsToChargesAsync(fixture.Zev.Id, Guid.NewGuid(),
            [charge.PersonId], CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(20m, Assert.Single(fixture.Database.PaymentAllocations).Amount);
        Assert.Single(fixture.Database.Payments);
        Assert.Equal(20m, fixture.Database.FundTransactions.Sum(item => item.SignedAmount));
    }

    [Fact]
    public async Task BillingCreditAllocationBatchesMoreThanOneHundredCharges()
    {
        using var fixture = new PaymentFixture(1, firstAmount: 1m);
        var first = fixture.Charges[0];
        for (var index = 0; index < 100; index++)
            fixture.Database.Charges.Add(new Charge { OrganizationId = first.OrganizationId,
                ZevId = first.ZevId, PersonId = first.PersonId, PropertyUnitId = first.PropertyUnitId,
                FundId = first.FundId, Amount = 1m, Period = first.Period, DueDate = first.DueDate,
                Reference = $"ZEV-{Guid.NewGuid():N}" });
        await fixture.Database.SaveChangesAsync();
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id,
            new ManualPaymentInput(Guid.NewGuid(), first.PersonId, null, first.FundId,
                new DateOnly(2026, 9, 1), 101m, "BAM", "Migration", "Opening credit", null, null, null),
            null, CancellationToken.None);
        Assert.True(created.Success, created.Error);
        var error = await workflow.ApplyAvailableCreditsToChargesAsync(fixture.Zev.Id, Guid.NewGuid(),
            [first.PersonId], CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(101, fixture.Database.PaymentAllocations.Count());
        Assert.Equal(101m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
    }

    [Fact]
    public async Task ManualPaymentAllocationRejectsOtherOwnerAndDifferentRequestPayload()
    {
        using var fixture = new PaymentFixture(2);
        var first = fixture.Charges[0];
        var second = fixture.Charges[1];
        var workflow = new PaymentWorkflowService(fixture.Database);
        var request = new ManualPaymentInput(Guid.NewGuid(), first.PersonId, null, first.FundId,
            new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Test", null, null,
            [new PaymentAllocationInput(first.Id, 40m)]);
        var created = await workflow.CreateManualAsync(fixture.Zev.Id, request, null, CancellationToken.None);
        Assert.True(created.Success, created.Error);
        var changedRetry = await workflow.CreateManualAsync(fixture.Zev.Id,
            request with { Allocations = [new PaymentAllocationInput(first.Id, 50m)] },
            null, CancellationToken.None);
        Assert.False(changedRetry.Success);
        var otherOwner = await workflow.AllocateAsync(fixture.Zev.Id, created.PaymentId!.Value,
            Guid.NewGuid(), [new PaymentAllocationInput(second.Id, 10m)], CancellationToken.None);
        Assert.False(otherOwner.Success);
        Assert.Single(fixture.Database.PaymentAllocations);
    }

    [Fact]
    public void DelimitedAndXmlParsersReadSupportedFormats()
    {
        var csv = new DelimitedStatementParser("CSV", ',').Parse("date,amount,reference,counterparty,description\n2026-09-01,12.50,R1,Test,Maintenance");
        var txt = new DelimitedStatementParser("TXT", ';').Parse("date;amount;reference\n01.09.2026;12.50;R1");
        var xml = new XmlStatementParser().Parse("<transactions><transaction><date>2026-09-01</date><amount>12.50</amount><reference>R1</reference></transaction></transactions>");
        Assert.Equal(12.50m, csv[0].Amount);
        Assert.Equal("R1", txt[0].Reference);
        Assert.Equal(new DateOnly(2026, 9, 1), xml[0].BookingDate);
    }

    [Fact]
    public void CamtParserReadsCreditAndReference()
    {
        const string content = "<Document><BkToCstmrStmt><Stmt><Ntry><Amt Ccy=\"BAM\">25.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><BookgDt><Dt>2026-09-01</Dt></BookgDt><NtryDtls><TxDtls><RmtInf><Strd><CdtrRefInf><Ref>ZEV-1</Ref></CdtrRefInf></Strd></RmtInf></TxDtls></NtryDtls></Ntry></Stmt></BkToCstmrStmt></Document>";
        var item = Assert.Single(new Camt053StatementParser().Parse(content));
        Assert.Equal(25m, item.Amount);
        Assert.Equal("ZEV-1", item.Reference);
    }

    [Fact]
    public async Task ExactReferencesMatchAndDuplicateStatementIsRejected()
    {
        using var fixture = new PaymentFixture(20);
        var content = "date,amount,reference\n" + string.Join("\n", fixture.Charges.Select((charge, index) =>
            $"2026-09-01,100.00,{charge.Reference}"));
        var bytes = Encoding.UTF8.GetBytes(content);
        var first = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "bank.csv", "CSV", bytes, CancellationToken.None);
        var second = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "bank.csv", "CSV", bytes, CancellationToken.None);
        Assert.Equal(20, first.MatchedCount);
        Assert.NotNull(first.Import);
        Assert.Null(second.Import);
        Assert.Equal(20, fixture.Database.PaymentAllocations.Count());
        Assert.Equal(2000m, fixture.Database.FundTransactions.Sum(item => item.SignedAmount));
    }

    [Fact]
    public async Task PreviewIsReadOnlyTenantScopedAndDetectsDuplicate()
    {
        using var fixture = new PaymentFixture(1);
        var bytes = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-09-01,25.00,{fixture.Charges[0].Reference}");
        var denied = await fixture.Engine.PreviewAsync(Guid.NewGuid(), fixture.Account.Id,
            "bank.csv", "CSV", bytes, CancellationToken.None);
        Assert.Null(denied.Rows);
        var preview = await fixture.Engine.PreviewAsync(fixture.Zev.Id, fixture.Account.Id,
            "bank.csv", "CSV", bytes, CancellationToken.None);
        Assert.Single(preview.Rows!);
        Assert.False(preview.IsDuplicate);
        Assert.Empty(fixture.Database.BankStatementImports);
        Assert.Empty(fixture.Database.Payments);
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "bank.csv", "CSV", bytes, CancellationToken.None);
        var duplicate = await fixture.Engine.PreviewAsync(fixture.Zev.Id, fixture.Account.Id,
            "bank.csv", "CSV", bytes, CancellationToken.None);
        Assert.True(duplicate.IsDuplicate);
    }

    [Fact]
    public async Task SyntheticUniqueReferenceMatchRateExceedsNinetyFivePercent()
    {
        using var fixture = new PaymentFixture(200);
        var lines = fixture.Charges.Select((charge, index) =>
            $"2026-09-01,{(index % 10 == 0 ? "40.00" : index % 10 == 1 ? "120.00" : "100.00")},{charge.Reference}")
            .ToList();
        lines.AddRange(Enumerable.Range(0, 10).Select(index => $"2026-09-01,50.00,WRONG-{index}"));
        lines.Add($"2026-09-01,100.00,{fixture.Charges[2].Reference}");
        var bytes = Encoding.UTF8.GetBytes("date,amount,reference\n" + string.Join("\n", lines));
        var result = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "synthetic.csv", "CSV", bytes, CancellationToken.None);
        const int technicallyValidUniqueReferences = 200;
        var successRate = (decimal)result.MatchedCount / technicallyValidUniqueReferences;
        Assert.True(successRate >= .95m);
        Assert.Equal(200, result.MatchedCount);
        Assert.Equal(11, fixture.Database.BankTransactions.Count(item => item.Status == BankTransactionStatus.ManualReview));
        Assert.Equal(200, fixture.Database.PaymentAllocations.Count());
    }

    [Fact]
    public async Task PartialPaymentAndOverpaymentPreserveUnallocatedRemainder()
    {
        using var fixture = new PaymentFixture(1);
        var reference = fixture.Charges[0].Reference;
        var first = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-09-01,40.00,{reference}");
        var second = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-09-02,120.00,{reference}");
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "first.csv", "CSV", first, CancellationToken.None);
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "second.csv", "CSV", second, CancellationToken.None);
        Assert.Equal(100m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(160m, fixture.Database.Payments.Sum(item => item.Amount));
    }

    [Fact]
    public async Task UnknownReferenceEntersQueueAndCanBeAllocatedManually()
    {
        using var fixture = new PaymentFixture(1);
        var bytes = Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-01,35.00,UNKNOWN");
        var imported = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "unknown.csv", "CSV", bytes, CancellationToken.None);
        var transaction = Assert.Single(fixture.Database.BankTransactions);
        Assert.Equal(0, imported.MatchedCount);
        Assert.Equal(BankTransactionStatus.ManualReview, transaction.Status);
        Assert.True(await fixture.Engine.AllocateManuallyAsync(fixture.Zev.Id, transaction.Id,
            [(fixture.Charges[0].Id, 35m)], CancellationToken.None));
        Assert.Equal(BankTransactionStatus.Matched, transaction.Status);
        Assert.Equal(35m, Assert.Single(fixture.Database.PaymentAllocations).Amount);
    }

    [Fact]
    public async Task ManualAllocationCannotExceedPaymentOrMixOwners()
    {
        using var fixture = new PaymentFixture(2);
        var bytes = Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-01,50.00,UNKNOWN");
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "manual.csv", "CSV", bytes, CancellationToken.None);
        var transactionId = Assert.Single(fixture.Database.BankTransactions).Id;
        Assert.False(await fixture.Engine.AllocateManuallyAsync(fixture.Zev.Id, transactionId,
            [(fixture.Charges[0].Id, 60m)], CancellationToken.None));
        Assert.False(await fixture.Engine.AllocateManuallyAsync(fixture.Zev.Id, transactionId,
            [(fixture.Charges[0].Id, 25m), (fixture.Charges[1].Id, 25m)], CancellationToken.None));
        Assert.True(await fixture.Engine.AllocateManuallyAsync(fixture.Zev.Id, transactionId,
            [(fixture.Charges[0].Id, 30m)], CancellationToken.None));
        Assert.False(await fixture.Engine.AllocateManuallyAsync(fixture.Zev.Id, transactionId,
            [(fixture.Charges[0].Id, 25m)], CancellationToken.None));
        Assert.Equal(30m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
    }

    [Fact]
    public async Task AmbiguousReferenceWithEqualAmountNeverChoosesAnOwner()
    {
        using var fixture = new PaymentFixture(2, duplicateReference: true);
        var bytes = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-09-01,100.00,{fixture.Charges[0].Reference}");
        var result = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "ambiguous.csv", "CSV", bytes, CancellationToken.None);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(BankTransactionStatus.ManualReview, Assert.Single(fixture.Database.BankTransactions).Status);
        Assert.Empty(fixture.Database.PaymentAllocations);
        Assert.Equal(ReconciliationStatus.Ambiguous, Assert.Single(fixture.Database.ReconciliationAttempts).Status);
    }

    [Fact]
    public async Task StableAccountReferenceAllocatesSeveralLinesAndKeepsCredit()
    {
        var unitId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, accountUnitId: unitId, firstAmount: 30m);
        var references = new PaymentReferenceService(fixture.Database);
        var first = fixture.Charges[0];
        foreach (var amount in new[] { 18m, 5m })
            fixture.Database.Charges.Add(new Charge { OrganizationId = first.OrganizationId,
                ZevId = first.ZevId, PropertyUnitId = unitId, PersonId = first.PersonId,
                FundId = first.FundId, Amount = amount, Period = first.Period,
                DueDate = first.DueDate, Reference = $"ZEV-{Guid.NewGuid():N}" });
        fixture.Database.SaveChanges();
        var reference = await references.EnsureAsync(fixture.Zev.Id, unitId, first.PersonId, CancellationToken.None);
        Assert.Equal(PaymentReferenceFormat.Length, reference.Length);
        Assert.True(reference.All(char.IsAsciiDigit));
        Assert.Equal(reference, await references.EnsureAsync(fixture.Zev.Id, unitId, first.PersonId, CancellationToken.None));
        var wrongCheckDigit = reference[..^1] + (char)('0' + (reference[^1] - '0' + 1) % 10);
        var resolved = await references.ResolveAsync(fixture.Zev.Id, [reference, wrongCheckDigit], CancellationToken.None);
        Assert.Equal(new PaymentAccountReference(fixture.Zev.Id, unitId, first.PersonId), resolved[reference]);
        Assert.False(resolved.ContainsKey(wrongCheckDigit));
        Assert.Empty(await references.ResolveAsync(Guid.NewGuid(), [reference], CancellationToken.None));
        var partial = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-10-10,30.00,{reference}");
        var result = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "partial.csv", "CSV", partial, CancellationToken.None);
        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(23m, fixture.Database.Charges.Sum(charge => charge.Amount)
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        var overpayment = Encoding.UTF8.GetBytes($"date,amount,reference\n2026-10-11,40.00,{reference}");
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "over.csv", "CSV", overpayment, CancellationToken.None);
        Assert.Equal(53m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(17m, fixture.Database.Payments.Sum(item => item.Amount)
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
    }

    [Fact]
    public async Task FullBankReturnPreservesOriginalRowsAndReopensCharge()
    {
        using var fixture = new PaymentFixture(1);
        var reference = fixture.Charges[0].Reference;
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "credit.csv", "CSV",
            Encoding.UTF8.GetBytes($"date,amount,reference\n2026-09-01,100.00,{reference}"), CancellationToken.None);
        var payment = Assert.Single(fixture.Database.Payments);
        await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "debit.csv", "CSV",
            Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-02,-100.00,RETURN"), CancellationToken.None);
        var returned = fixture.Database.BankTransactions.Single(item => item.Amount < 0m);
        var reversal = await fixture.Engine.ReverseFullPaymentAsync(fixture.Zev.Id, returned.Id,
            payment.Id, "Banka vratila uplatu", CancellationToken.None);
        Assert.True(reversal.Success, reversal.Error);
        Assert.Equal(PaymentStatus.Reversed, payment.Status);
        Assert.Equal(new DateOnly(2026, 9, 2), payment.ReversedOn);
        Assert.Equal(BankTransactionStatus.Returned, returned.Status);
        Assert.Single(fixture.Database.PaymentAllocations);
        Assert.Equal(100m, fixture.Database.Charges.Sum(item => item.Amount)
            - fixture.Database.PaymentAllocations.Where(item => item.Payment.Status == PaymentStatus.Posted)
                .Sum(item => item.Amount));
        Assert.Equal(0m, fixture.Database.FundTransactions.Sum(item => item.SignedAmount));
        Assert.Equal(100m, fixture.Database.PaymentAllocations.Where(item =>
            item.Payment.PaidOn <= new DateOnly(2026, 9, 1)
            && (item.Payment.Status == PaymentStatus.Posted
                || item.Payment.ReversedOn > new DateOnly(2026, 9, 1))).Sum(item => item.Amount));
        Assert.Equal(0m, fixture.Database.PaymentAllocations.Where(item =>
            item.Payment.PaidOn <= new DateOnly(2026, 9, 2)
            && (item.Payment.Status == PaymentStatus.Posted
                || item.Payment.ReversedOn > new DateOnly(2026, 9, 2))).Sum(item => item.Amount));
        Assert.False((await fixture.Engine.ReverseFullPaymentAsync(fixture.Zev.Id, returned.Id,
            payment.Id, "Duplicate", CancellationToken.None)).Success);
    }

    [Fact]
    public async Task MixedFundAccountReferenceStopsAtBankAccountFund()
    {
        var unitId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, accountUnitId: unitId, firstAmount: 30m);
        var first = fixture.Charges[0];
        var reserve = new Fund { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
            Kind = FundKind.Reserve, Name = "Reserve" };
        fixture.Database.AddRange(reserve,
            new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                PropertyUnitId = unitId, PersonId = first.PersonId, FundId = first.FundId,
                Amount = 5m, Reference = $"ZEV-{Guid.NewGuid():N}" },
            new Charge { OrganizationId = first.OrganizationId, ZevId = first.ZevId,
                PropertyUnitId = unitId, PersonId = first.PersonId, FundId = reserve.Id,
                Amount = 18m, Reference = $"ZEV-{Guid.NewGuid():N}" });
        fixture.Database.SaveChanges();
        // Issued invoices before the short numeric format keep the legacy reference; statements quoting it still match.
        var reference = LegacyPaymentReference.Format(fixture.Zev.Id, unitId, first.PersonId);
        var imported = await fixture.Engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id, "mixed.csv", "CSV",
            Encoding.UTF8.GetBytes($"date,amount,reference\n2026-10-10,53.00,{reference}"), CancellationToken.None);
        Assert.Equal(BankImportStatus.PartiallyReconciled, imported.Import?.Status);
        Assert.Equal(35m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(18m, fixture.Database.Payments.Sum(item => item.Amount)
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(BankTransactionStatus.PartiallyMatched,
            Assert.Single(fixture.Database.BankTransactions).Status);
    }

    [Fact]
    public async Task StatementImportStoresAuthenticatedImporter()
    {
        using var fixture = new PaymentFixture(1);
        var userId = Guid.NewGuid();
        var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())]));
        var engine = new PaymentReconciliationEngine(fixture.Database,
            [new DelimitedStatementParser("CSV", ',')], context);
        var result = await engine.ImportAsync(fixture.Zev.Id, fixture.Account.Id,
            "importer.csv", "CSV", Encoding.UTF8.GetBytes("date,amount,reference\n2026-10-10,5.00,UNKNOWN"),
            CancellationToken.None);
        Assert.Equal(userId, result.Import?.ImportedByUserId);
    }

    [Fact]
    public async Task FullAllocationReversalKeepsHistoricalOwnerUnitAndInvoiceBalances()
    {
        var unitId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, accountUnitId: unitId,
            chargePeriod: new DateOnly(2026, 8, 1),
            chargeCreatedUtc: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var charge = fixture.Charges[0];
        var run = new BillingRun { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            Period = charge.Period, DueDate = new DateOnly(2026, 8, 31), TotalAmount = 100m,
            ChargeCount = 1 };
        var invoice = new Invoice { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            BillingRunId = run.Id, PropertyUnitId = unitId, PersonId = charge.PersonId,
            InvoiceNumber = "ASOF-2026-1", NumberYear = 2026, NumberSequence = 1,
            Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
            CurrentChargeTotal = 100m, TotalAmount = 100m, TotalDueAtIssue = 100m,
            Currency = "BAM" };
        var line = new InvoiceLine { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            Invoice = invoice, Charge = charge, FundId = charge.FundId,
            CodeSnapshot = "ASOF", DescriptionSnapshot = "Test", Quantity = 1m,
            UnitPrice = 100m, AmountBeforeDiscount = 100m, FinalAmount = 100m };
        var payment = new Payment { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            FundId = charge.FundId, PersonId = charge.PersonId, Amount = 100m,
            CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 8, 10),
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
        var allocation = new PaymentAllocation { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, Payment = payment, Charge = charge, Amount = 100m,
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero) };
        var reversal = new PaymentAllocation { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, Payment = payment, Charge = charge, Amount = -100m,
            ReversesAllocationId = allocation.Id,
            CreatedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        fixture.Database.AddRange(run, invoice, line, payment, allocation, reversal);
        await fixture.Database.SaveChangesAsync();
        var reader = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        foreach (var (asOf, open, credit) in new[]
            { (new DateOnly(2026, 8, 31), 0m, 0m), (new DateOnly(2026, 9, 6), 100m, 100m) })
        {
            var owner = await reader.ReadAsync(charge.ZevId, charge.PersonId, null, asOf,
                1, 10, CancellationToken.None);
            var unit = await reader.ReadUnitAsync(charge.ZevId, unitId, charge.PersonId,
                null, asOf, 1, 10, CancellationToken.None);
            var invoiceCard = await reader.ReadInvoiceAsync(charge.ZevId, invoice.Id, asOf,
                CancellationToken.None);
            var invoiceState = (await FinancialReadModel.InvoiceStatesAsync(fixture.Database,
                charge.ZevId, [invoice.Id], asOf, CancellationToken.None))[invoice.Id];
            Assert.Equal(open, Assert.Single(owner.Balances).Open);
            Assert.Equal(credit, Assert.Single(owner.Balances).Credit);
            Assert.Equal(open, Assert.Single(unit.Balances).Open);
            Assert.Equal(open, Assert.Single(invoiceCard.Balances).Open);
            Assert.Equal(open, Assert.Single(invoiceState.Balances).Open);
            Assert.Equal(0m, Assert.Single(unit.Balances).Credit);
        }
    }

    [Fact]
    public async Task LegacyCurrencyIsUnknownUntilHistoricalAllocationIdentifiesIt()
    {
        using var fixture = new PaymentFixture(1, chargePeriod: new DateOnly(2026, 8, 1),
            chargeCreatedUtc: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var charge = fixture.Charges[0];
        var reader = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var beforePayment = await reader.ReadAsync(charge.ZevId, charge.PersonId,
            null, new DateOnly(2026, 8, 5), 1, 10, CancellationToken.None);
        Assert.Equal("UNKNOWN", Assert.Single(beforePayment.Balances).CurrencyCode);
        Assert.Equal(100m, Assert.Single(beforePayment.Balances).Open);
        var payment = new Payment { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            FundId = charge.FundId, PersonId = charge.PersonId, Amount = 20m,
            CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 8, 10),
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
        fixture.Database.AddRange(payment, new PaymentAllocation
        { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            Payment = payment, Charge = charge, Amount = 20m,
            CreatedUtc = new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero) });
        await fixture.Database.SaveChangesAsync();
        var historical = await reader.ReadAsync(charge.ZevId, charge.PersonId,
            null, new DateOnly(2026, 8, 5), 1, 10, CancellationToken.None);
        Assert.Equal("UNKNOWN", Assert.Single(historical.Balances).CurrencyCode);
        var afterPayment = await reader.ReadAsync(charge.ZevId, charge.PersonId,
            null, new DateOnly(2026, 8, 31), 1, 10, CancellationToken.None);
        var balance = Assert.Single(afterPayment.Balances);
        Assert.Equal("BAM", balance.CurrencyCode);
        Assert.Equal((100m, 20m, 80m, 0m),
            (balance.TotalCharges, balance.TotalPayments, balance.Open, balance.Credit));
    }

    [Fact]
    public async Task FullInvoiceStornoReleasesPaymentCreditWithoutUnitCredit()
    {
        var unitId = Guid.NewGuid();
        using var fixture = new PaymentFixture(1, accountUnitId: unitId);
        var charge = fixture.Charges[0];
        var run = new BillingRun { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            Period = charge.Period, DueDate = charge.DueDate, TotalAmount = 100m,
            ChargeCount = 1 };
        var invoice = new Invoice { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            BillingRunId = run.Id, PropertyUnitId = unitId, PersonId = charge.PersonId,
            InvoiceNumber = "STORNO-2026-1", NumberYear = 2026, NumberSequence = 1,
            Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
            CurrentChargeTotal = 100m, TotalAmount = 100m, TotalDueAtIssue = 100m,
            Currency = "BAM", DocumentStatus = InvoiceDocumentStatus.Cancelled };
        var line = new InvoiceLine { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            Invoice = invoice, Charge = charge, FundId = charge.FundId,
            CodeSnapshot = "STORNO", DescriptionSnapshot = "Test", Quantity = 1m,
            UnitPrice = 100m, AmountBeforeDiscount = 100m, FinalAmount = 100m };
        var payment = new Payment { OrganizationId = charge.OrganizationId, ZevId = charge.ZevId,
            FundId = charge.FundId, PersonId = charge.PersonId, Amount = 100m,
            CurrencyCode = "BAM", PaidOn = charge.Period };
        var allocation = new PaymentAllocation { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, Payment = payment, Charge = charge, Amount = 100m };
        var storno = new FinancialDocument { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, OriginalInvoice = invoice, Type = FinancialDocumentType.Storno,
            SignedAmount = -100m, Currency = "BAM", DocumentNumber = "STO-2026-1",
            NumberYear = 2026, NumberSequence = 1 };
        var cancellation = new Charge { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, PropertyUnitId = unitId, PersonId = charge.PersonId,
            FundId = charge.FundId, FinancialDocumentId = storno.Id,
            ReversesChargeId = charge.Id, Period = charge.Period,
            Amount = -100m, Reference = "STORNO-CHARGE" };
        var stornoLine = new FinancialDocumentLine { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, Document = storno, Charge = cancellation,
            FundId = charge.FundId, SignedAmount = -100m };
        var release = new PaymentAllocation { OrganizationId = charge.OrganizationId,
            ZevId = charge.ZevId, Payment = payment, Charge = charge, Amount = -100m,
            ReversesAllocationId = allocation.Id, FinancialDocumentId = storno.Id };
        fixture.Database.AddRange(run, invoice, line, payment, allocation,
            storno, cancellation, stornoLine, release);
        await fixture.Database.SaveChangesAsync();
        var reader = new OwnerCardService(fixture.Database, new ConfigurationBuilder().Build());
        var owner = await reader.ReadAsync(charge.ZevId, charge.PersonId,
            null, null, 1, 20, CancellationToken.None);
        var unit = await reader.ReadUnitAsync(charge.ZevId, unitId, charge.PersonId,
            null, null, 1, 20, CancellationToken.None);
        var invoiceCard = await reader.ReadInvoiceAsync(charge.ZevId, invoice.Id, null,
            CancellationToken.None);
        var invoiceState = (await FinancialReadModel.InvoiceStatesAsync(fixture.Database,
            charge.ZevId, [invoice.Id], null, CancellationToken.None))[invoice.Id];
        Assert.Equal(0m, Assert.Single(owner.Balances).Open);
        Assert.Equal(100m, Assert.Single(owner.Balances).Credit);
        Assert.Equal(0m, Assert.Single(unit.Balances).Open);
        Assert.Equal(0m, Assert.Single(unit.Balances).Credit);
        Assert.Equal(0m, Assert.Single(invoiceCard.Balances).Open);
        Assert.Equal(100m, invoiceState.StornoCredit);
        Assert.Equal(100m, invoiceState.PaidBeforeStorno);
        Assert.Equal(100m, FinancialReadModel.CalculatePaymentCredit(payment.Amount,
            fixture.Database.PaymentAllocations.Where(item => item.PaymentId == payment.Id)
                .Sum(item => item.Amount)));
    }

    [Fact]
    public async Task FundMovementsKeepPaymentAndReturnCurrencyButNotGuessExpenseCurrency()
    {
        using var fixture = new PaymentFixture(0);
        var payment = new Payment { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Account.FundId,
            Amount = 100m, CurrencyCode = "EUR", PaidOn = new DateOnly(2026, 8, 1) };
        var received = new FundTransaction { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Account.FundId,
            Kind = FundTransactionKind.Payment, SourceId = payment.Id,
            SignedAmount = 100m };
        var returned = new FundTransaction { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Account.FundId,
            Kind = FundTransactionKind.Reversal, ReversesTransaction = received,
            SourceId = Guid.NewGuid(), SignedAmount = -100m };
        var expense = new FundTransaction { OrganizationId = fixture.Zev.OrganizationId,
            ZevId = fixture.Zev.Id, FundId = fixture.Account.FundId,
            Kind = FundTransactionKind.Expense, SourceId = Guid.NewGuid(),
            SignedAmount = -20m };
        fixture.Database.AddRange(payment, received, returned, expense);
        await fixture.Database.SaveChangesAsync();
        var movements = await FinancialReadModel.FundMovements(fixture.Database, fixture.Zev.Id)
            .ToListAsync();
        Assert.Equal("EUR", movements.Single(item => item.Amount == 100m).CurrencyCode);
        Assert.Equal("EUR", movements.Single(item => item.Amount == -100m).CurrencyCode);
        Assert.Equal("UNKNOWN", movements.Single(item => item.Amount == -20m).CurrencyCode);
    }

    [Fact]
    public void FinancialReadQueriesTranslateForSqlServerWithoutExecutingThem()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var zevId = Guid.NewGuid();
        var charges = FinancialReadModel.Charges(database, zevId, new DateOnly(2026, 8, 31));
        var open = FinancialReadModel.OpenItems(database, zevId, new DateOnly(2026, 8, 31));
        var funds = FinancialReadModel.FundMovements(database, zevId);
        Assert.Contains("SELECT", charges.GroupBy(item => item.CurrencyCode)
            .Select(group => new { group.Key, Amount = group.Sum(item => item.Amount) })
            .ToQueryString());
        Assert.Contains("SELECT", open.Where(item => item.Amount + item.Correction > item.Allocated)
            .OrderBy(item => item.DueDate).Take(20).ToQueryString());
        Assert.Contains("SELECT", funds.GroupBy(item => new { item.FundId, item.CurrencyCode })
            .Select(group => new { group.Key.FundId, group.Key.CurrencyCode,
                Amount = group.Sum(item => item.Amount) }).ToQueryString());
    }

    private sealed class PaymentFixture : IDisposable
    {
        public ZevDbContext Database { get; }
        public PaymentReconciliationEngine Engine { get; }
        public global::Zev.Api.Domain.Zev Zev { get; }
        public BankAccount Account { get; }
        public List<Charge> Charges { get; } = [];

        public PaymentFixture(int chargeCount, bool duplicateReference = false,
            Guid? accountUnitId = null, decimal firstAmount = 100m, Guid? actorId = null,
            DateOnly? chargePeriod = null, DateTimeOffset? chargeCreatedUtc = null)
        {
            var options = new DbContextOptionsBuilder<ZevDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            if (actorId is Guid actor) context.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", actor.ToString())]));
            Database = new ZevDbContext(options, context);
            var organization = new Organization { Code = "TEST", Name = "Test" };
            Zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
                Organization = organization, Code = "ZEV", Name = "ZEV" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = Zev.Id,
                Kind = FundKind.Regular, Name = "Regular" };
            Account = new BankAccount { OrganizationId = organization.Id, ZevId = Zev.Id,
                FundId = fund.Id, Fund = fund, AccountNumber = "123", CurrencyCode = "BAM" };
            Database.AddRange(organization, Zev, fund, Account);
            for (var index = 0; index < chargeCount; index++)
            {
                var person = new Person { OrganizationId = organization.Id, DisplayName = $"Owner {index}" };
                var charge = new Charge { OrganizationId = organization.Id, ZevId = Zev.Id,
                    PersonId = person.Id, FundId = fund.Id, PropertyUnitId = accountUnitId ?? Guid.Empty,
                    Amount = index == 0 ? firstAmount : 100m,
                    Reference = $"ZEV-{Guid.NewGuid():N}",
                    Period = chargePeriod ?? new DateOnly(2026, 9, 1),
                    CreatedUtc = chargeCreatedUtc ?? DateTimeOffset.UtcNow };
                Database.Add(person);
                Database.Add(charge);
                Charges.Add(charge);
            }
            if (duplicateReference && Charges.Count > 1) Charges[1].Reference = Charges[0].Reference;
            Database.SaveChanges();
            Engine = new PaymentReconciliationEngine(Database, [new DelimitedStatementParser("CSV", ',')]);
        }

        public void Dispose() => Database.Dispose();
    }
}
