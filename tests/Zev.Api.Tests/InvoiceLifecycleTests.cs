using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Payments;

namespace Zev.Api.Tests;

public sealed class InvoiceLifecycleTests
{
    [Fact]
    public async Task StornoCreditReusesOriginalPaymentForNewCharge()
    {
        using var fixture = new LifecycleFixture(0);
        var workflow = new PaymentWorkflowService(fixture.Database);
        var created = await workflow.CreateManualAsync(fixture.ZevId,
            new ManualPaymentInput(Guid.NewGuid(), fixture.OriginalCharge.PersonId, null,
                fixture.FundId, new DateOnly(2026, 9, 1), 100m, "BAM", "Migration",
                "Original payment", null, null,
                [new PaymentAllocationInput(fixture.OriginalCharge.Id, 100m)]), null, CancellationToken.None);
        var payment = fixture.Database.Payments.Single(item => item.Id == created.PaymentId);
        var result = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Pogrešan račun", null), null, CancellationToken.None);
        Assert.NotNull(result.Document);
        Assert.Equal(0m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        var newCharge = new Charge { OrganizationId = fixture.OriginalCharge.OrganizationId,
            ZevId = fixture.ZevId, PropertyUnitId = fixture.OriginalCharge.PropertyUnitId,
            PersonId = fixture.OriginalCharge.PersonId, FundId = fixture.FundId,
            Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
            Amount = 60m, Reference = "NEW-CHARGE" };
        fixture.Database.Charges.Add(newCharge);
        await fixture.Database.SaveChangesAsync();
        var applied = await workflow.AllocateAsync(fixture.ZevId,
            payment.Id, Guid.NewGuid(), [new PaymentAllocationInput(newCharge.Id, 60m)],
            CancellationToken.None);
        Assert.True(applied.Success, applied.Error);
        Assert.Single(fixture.Database.Payments);
        Assert.Equal(0m, newCharge.Amount - fixture.Database.PaymentAllocations
            .Where(item => item.ChargeId == newCharge.Id).Sum(item => item.Amount));
        Assert.Equal(40m, payment.Amount - fixture.Database.PaymentAllocations
            .Where(item => item.PaymentId == payment.Id).Sum(item => item.Amount));
    }
    [Fact]
    public void DocumentNumberProviderUsesIndependentConfiguredSeries()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Finance:InvoiceNumberPrefix"] = "INV",
            ["Finance:StornoNumberPrefix"] = "STORNO",
            ["Finance:CorrectionNumberPrefix"] = "ISPR"
        }).Build();
        var provider = new InvoiceNumberProvider(configuration);
        Assert.Equal("STORNO-2026-000007", provider.Format(FinancialDocumentSeries.Storno, 2026, 7));
        Assert.Equal("ISPR-2026-000001", provider.Format(FinancialDocumentSeries.Correction, 2026, 1));
        var invalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Finance:StornoNumberPrefix"] = "INV"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new InvoiceNumberProvider(invalid)
            .Format(FinancialDocumentSeries.Storno, 2026, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public async Task StornoReversesDebtAndReleasesPaidAmountWithoutDeletingHistory(int paid)
    {
        using var fixture = new LifecycleFixture(paid);
        var originalPdfValues = (fixture.Invoice.InvoiceNumber, fixture.Invoice.TotalAmount,
            fixture.Invoice.PersonNameSnapshot, fixture.Line.DescriptionSnapshot);
        var result = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Pogrešan račun", null), null, CancellationToken.None);
        var document = Assert.IsType<FinancialDocument>(result.Document);
        Assert.Equal(FinancialDocumentType.Storno, document.Type);
        Assert.StartsWith("STO-", document.DocumentNumber);
        Assert.Equal(-100m, document.SignedAmount);
        Assert.Equal(InvoiceDocumentStatus.Cancelled, fixture.Invoice.DocumentStatus);
        Assert.Equal(0m, fixture.Database.Charges.Sum(item => item.Amount));
        Assert.Equal(100m, fixture.OriginalCharge.Amount);
        Assert.Single(fixture.Database.Charges.Where(item => item.ReversesChargeId == fixture.OriginalCharge.Id));
        Assert.Equal((decimal)paid, fixture.Database.Payments.Sum(item => item.Amount));
        Assert.Equal(0m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal((decimal)paid, fixture.Database.Payments.Sum(item => item.Amount)
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(originalPdfValues, (fixture.Invoice.InvoiceNumber, fixture.Invoice.TotalAmount,
            fixture.Invoice.PersonNameSnapshot, fixture.Line.DescriptionSnapshot));
        Assert.True(fixture.Database.AuditLogs.Any(item => item.EntityType == nameof(FinancialDocument)));
    }

    [Fact]
    public async Task FullyDiscountedInvoiceCanBeCancelledWithZeroAmountDocument()
    {
        using var fixture = new LifecycleFixture(0, 0m);
        var result = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Pogrešan račun", null), null, CancellationToken.None);
        var document = Assert.IsType<FinancialDocument>(result.Document);
        Assert.Equal(0m, document.SignedAmount);
        Assert.Empty(document.Lines);
        Assert.Equal(InvoiceDocumentStatus.Cancelled, fixture.Invoice.DocumentStatus);
        Assert.Single(fixture.Database.Charges);
        Assert.Empty(fixture.Database.PaymentAllocations);
    }

    [Fact]
    public async Task StornoReportsConflictOnlyForConcurrentWritesAndPropagatesUnexpectedFailures()
    {
        var failures = new FailingSaveInterceptor();
        using var fixture = new LifecycleFixture(0, failures: failures);

        failures.Next = new DbUpdateException("Unexpected provider failure.", new InvalidOperationException("FK"));
        var unexpected = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.StornoAsync(fixture.ZevId,
            fixture.Invoice.Id, new StornoRequest(Guid.NewGuid(), "Pogreška", null), null, CancellationToken.None));
        Assert.Equal("FK", unexpected.InnerException?.Message);
        fixture.Database.ChangeTracker.Clear();

        failures.Next = new DbUpdateConcurrencyException("Invoice changed by another writer.");
        var conflict = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Pogreška", null), null, CancellationToken.None);
        Assert.Null(conflict.Document);
        Assert.Equal("Document was concurrently posted or conflicts with an existing document.", conflict.Error);
        fixture.Database.ChangeTracker.Clear();
        Assert.Empty(fixture.Database.FinancialDocuments);
    }

    [Fact]
    public async Task RepeatedStornoRequestIsIdempotentAndSecondRequestIsRejected()
    {
        using var fixture = new LifecycleFixture(25);
        var request = new StornoRequest(Guid.NewGuid(), "Pogreška", null);
        var first = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id, request,
            null, CancellationToken.None);
        var retry = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id, request,
            null, CancellationToken.None);
        var divergent = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            request with { Reason = "Drugi razlog" }, null, CancellationToken.None);
        var second = await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Drugi pokušaj", null), null, CancellationToken.None);
        Assert.True(retry.AlreadyExists);
        Assert.Equal(first.Document!.Id, retry.Document!.Id);
        Assert.Null(divergent.Document);
        Assert.Null(second.Document);
        Assert.Single(fixture.Database.FinancialDocuments);
    }

    [Fact]
    public async Task CreditAndDebitCorrectionsPreserveOriginalAndCreateSignedCharges()
    {
        using var fixture = new LifecycleFixture(80);
        var credit = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Credit, "Umanjenje", null,
                [new CorrectionLineRequest(fixture.Line.Id, null, null, 30m)]), null, CancellationToken.None);
        Assert.NotNull(credit.Document);
        Assert.Equal(-30m, credit.Document.SignedAmount);
        Assert.Equal(70m, fixture.Database.Charges.Sum(item => item.Amount));
        Assert.Equal(70m, fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(10m, fixture.Database.Payments.Sum(item => item.Amount)
            - fixture.Database.PaymentAllocations.Sum(item => item.Amount));
        Assert.Equal(InvoiceDocumentStatus.Corrected, fixture.Invoice.DocumentStatus);
        var debit = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Debit, "Dodatni radovi", null,
                [new CorrectionLineRequest(null, fixture.FundId, "Dodatna stavka", 15m)]),
            null, CancellationToken.None);
        Assert.NotNull(debit.Document);
        Assert.StartsWith("COR-", debit.Document.DocumentNumber);
        Assert.Equal(85m, fixture.Database.Charges.Sum(item => item.Amount));
        Assert.Equal(100m, fixture.Invoice.TotalAmount);
        Assert.Equal(100m, fixture.Line.FinalAmount);
        Assert.Equal(2, fixture.Database.FinancialDocuments.Count());
        Assert.Equal(2, fixture.Database.FinancialDocumentLines.Count());
        Assert.Null((await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Kasni storno", null), null, CancellationToken.None)).Document);
    }

    [Fact]
    public async Task MultiLineCreditAndDebitUseOneDocumentPerRequest()
    {
        using var fixture = new LifecycleFixture(0, splitLines: true);
        var second = Assert.IsType<InvoiceLine>(fixture.SecondLine);
        var credit = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Credit, "Dvije stavke", null,
                [new CorrectionLineRequest(fixture.Line.Id, null, null, 10m),
                    new CorrectionLineRequest(second.Id, null, null, 5m)]), null, CancellationToken.None);
        Assert.Equal(-15m, credit.Document!.SignedAmount);
        Assert.Equal(2, credit.Document.Lines.Count);
        Assert.Equal(85m, fixture.Database.Charges.Sum(item => item.Amount));

        var debit = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Debit, "Dopuna", null,
                [new CorrectionLineRequest(fixture.Line.Id, null, null, 7m),
                    new CorrectionLineRequest(null, fixture.FundId, "Nova stavka", 3m)]),
            null, CancellationToken.None);
        Assert.Equal(10m, debit.Document!.SignedAmount);
        Assert.Equal(2, debit.Document.Lines.Count);
        Assert.Equal(95m, fixture.Database.Charges.Sum(item => item.Amount));
        Assert.Equal(100m, fixture.Invoice.TotalAmount);
        Assert.Equal(InvoiceDocumentStatus.Corrected, fixture.Invoice.DocumentStatus);
        Assert.Null((await fixture.Service.StornoAsync(fixture.ZevId, fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Direktan storno", null), null, CancellationToken.None)).Document);
    }

    [Fact]
    public async Task CreditCannotExceedOriginalLineAndTenantCannotTouchInvoice()
    {
        using var fixture = new LifecycleFixture(0);
        var wrongTenant = await fixture.Service.StornoAsync(Guid.NewGuid(), fixture.Invoice.Id,
            new StornoRequest(Guid.NewGuid(), "Pogreška", null), null, CancellationToken.None);
        Assert.Null(wrongTenant.Document);
        var tooLarge = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Credit, "Prevelik iznos", null,
                [new CorrectionLineRequest(fixture.Line.Id, null, null, 101m)]), null, CancellationToken.None);
        Assert.Null(tooLarge.Document);
        Assert.Empty(fixture.Database.FinancialDocuments);
    }

    [Fact]
    public async Task PostedInvoiceSnapshotAndCorrectionDocumentAreImmutable()
    {
        using var fixture = new LifecycleFixture(0);
        var result = await fixture.Service.CorrectAsync(fixture.ZevId, fixture.Invoice.Id,
            new CorrectionRequest(Guid.NewGuid(), FinancialDocumentType.Debit, "Dodatak", null,
                [new CorrectionLineRequest(fixture.Line.Id, null, null, 5m)]), null, CancellationToken.None);
        fixture.Invoice.TotalAmount = 105m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Database.SaveChangesAsync());
        fixture.Database.Entry(fixture.Invoice).Reload();
        result.Document!.Reason = "Promjena";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Database.SaveChangesAsync());
    }

    [Fact]
    public async Task LegacyProjectionCannotBeCancelled()
    {
        using var fixture = new LifecycleFixture(0);
        var result = await fixture.Service.StornoAsync(fixture.ZevId, Guid.NewGuid(),
            new StornoRequest(Guid.NewGuid(), "Legacy", null), null, CancellationToken.None);
        Assert.Null(result.Document);
        Assert.Empty(fixture.Database.FinancialDocuments);
    }

    [Fact]
    public async Task InvoiceCannotBeMarkedCancelledWithoutStornoDocument()
    {
        using var fixture = new LifecycleFixture(0);
        fixture.Invoice.DocumentStatus = InvoiceDocumentStatus.Cancelled;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Database.SaveChangesAsync());
        Assert.Empty(fixture.Database.FinancialDocuments);
    }

    private sealed class LifecycleFixture : IDisposable
    {
        public ZevDbContext Database { get; }
        public InvoiceLifecycleService Service { get; }
        public Guid ZevId { get; }
        public Guid FundId { get; }
        public Invoice Invoice { get; }
        public InvoiceLine Line { get; }
        public InvoiceLine? SecondLine { get; }
        public Charge OriginalCharge { get; }

        public LifecycleFixture(int paid, decimal total = 100m, bool splitLines = false,
            FailingSaveInterceptor? failures = null)
        {
            var options = new DbContextOptionsBuilder<ZevDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(failures ?? new FailingSaveInterceptor()).Options;
            Database = new ZevDbContext(options, new HttpContextAccessor());
            var organization = new Organization { Code = "LIFECYCLE", Name = "Lifecycle" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization,
                Code = "LIFECYCLE", Name = "Lifecycle" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev,
                Code = "B", Name = "Building" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "Entrance" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "A1", AreaSquareMeters = 60m };
            var person = new Person { Organization = organization, DisplayName = "Original person" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var run = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 30),
                TotalAmount = total, ChargeCount = splitLines ? 2 : 1 };
            OriginalCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = splitLines ? 60m : total,
                Reference = "ZEV-ORIGINAL" };
            Invoice = new Invoice { OrganizationId = organization.Id, ZevId = zev.Id,
                BillingRunId = run.Id, PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = "INV-2026-000001", NumberYear = 2026, NumberSequence = 1,
                Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
                TotalAmount = total, CurrentChargeTotal = total,
                PersonNameSnapshot = "Original person", ZevNameSnapshot = "Lifecycle",
                UnitCodeSnapshot = "A1", UnitTypeSnapshot = "Apartment" };
            Line = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = Invoice, Charge = OriginalCharge, FundId = fund.Id,
                FundNameSnapshot = fund.Name, CodeSnapshot = "FIX", DescriptionSnapshot = "Regular",
                UnitOfMeasureSnapshot = "kom", Quantity = 1m, UnitPrice = OriginalCharge.Amount,
                AmountBeforeDiscount = OriginalCharge.Amount, FinalAmount = OriginalCharge.Amount };
            Database.AddRange(organization, zev, building, entrance, unit, person, fund,
                run, OriginalCharge, Invoice, Line);
            if (splitLines)
            {
                var secondCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                    Period = run.Period, DueDate = run.DueDate, Amount = 40m, Reference = "ZEV-SECOND" };
                SecondLine = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                    Invoice = Invoice, Charge = secondCharge, FundId = fund.Id,
                    FundNameSnapshot = fund.Name, CodeSnapshot = "SECOND", DescriptionSnapshot = "Second",
                    UnitOfMeasureSnapshot = "kom", Quantity = 1m, UnitPrice = 40m,
                    AmountBeforeDiscount = 40m, FinalAmount = 40m };
                Database.AddRange(secondCharge, SecondLine);
            }
            if (paid > 0)
            {
                var payment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                    PersonId = person.Id, FundId = fund.Id, Amount = paid, PaidOn = run.Period,
                    CurrencyCode = "BAM", BankTransactionId = Guid.NewGuid() };
                Database.Add(payment);
                Database.Add(new PaymentAllocation { OrganizationId = organization.Id, ZevId = zev.Id,
                    Payment = payment, Charge = OriginalCharge, Amount = paid });
            }
            Database.SaveChanges();
            ZevId = zev.Id; FundId = fund.Id;
            Service = new InvoiceLifecycleService(Database, new InvoiceNumberProvider());
        }

        public void Dispose() => Database.Dispose();
    }
}
