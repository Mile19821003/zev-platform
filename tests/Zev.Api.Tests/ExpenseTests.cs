using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Storage;

namespace Zev.Api.Tests;

public sealed class ExpenseTests
{
    [Fact]
    public async Task PostingAndReversalUseOppositeLedgerEntriesWithoutDeletingHistory()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "ORG", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "ZEV", Name = "ZEV" };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Regular" };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Supplier" };
        var expense = new Expense { OrganizationId = organization.Id, ZevId = zev.Id, FundId = fund.Id,
            SupplierId = supplier.Id, InvoiceNumber = "INV-1", Category = "Repair",
            InvoiceDate = new DateOnly(2026, 9, 1), Amount = 125.50m };
        database.AddRange(organization, zev, fund, supplier, expense);
        await database.SaveChangesAsync();
        var service = new ExpensePostingService(database);

        Assert.True(await service.PostAsync(zev.Id, expense.Id, CancellationToken.None));
        Assert.Equal(-125.50m, database.FundTransactions.Sum(item => item.SignedAmount));
        Assert.True(await service.ReverseAsync(zev.Id, expense.Id, CancellationToken.None));
        Assert.Equal(0m, database.FundTransactions.Sum(item => item.SignedAmount));
        Assert.Equal(2, database.FundTransactions.Count());
        Assert.False(await service.ReverseAsync(zev.Id, expense.Id, CancellationToken.None));
        database.Expenses.Remove(expense);
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        database.Entry(expense).State = EntityState.Unchanged;
        Assert.Throws<InvalidOperationException>(() =>
        {
            var transaction = database.FundTransactions.First();
            transaction.SignedAmount = -1m;
            database.SaveChanges();
        });
    }

    [Fact]
    public async Task PostingTreatsOnlyConcurrentWriteConflictsAsBusinessConflict()
    {
        var failures = new FailingSaveInterceptor();
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(failures).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "ORG", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "ZEV", Name = "ZEV" };
        var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Regular" };
        var supplier = new Supplier { OrganizationId = organization.Id, Name = "Supplier" };
        var expense = new Expense { OrganizationId = organization.Id, ZevId = zev.Id, FundId = fund.Id,
            SupplierId = supplier.Id, InvoiceNumber = "INV-1", Category = "Repair",
            InvoiceDate = new DateOnly(2026, 9, 1), Amount = 10m };
        database.AddRange(organization, zev, fund, supplier, expense);
        await database.SaveChangesAsync();
        var service = new ExpensePostingService(database);

        failures.Next = new DbUpdateConcurrencyException("Row changed by another writer.");
        Assert.False(await service.PostAsync(zev.Id, expense.Id, CancellationToken.None));
        database.ChangeTracker.Clear();

        failures.Next = new DbUpdateException("Unexpected provider failure.", new InvalidOperationException("FK"));
        var unexpected = await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.PostAsync(zev.Id, expense.Id, CancellationToken.None));
        Assert.Equal("FK", unexpected.InnerException?.Message);
        database.ChangeTracker.Clear();

        failures.Next = new InvalidOperationException("Transient failure.",
            new DbUpdateException("Timeout.", new TimeoutException()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(zev.Id, expense.Id, CancellationToken.None));
        database.ChangeTracker.Clear();

        Assert.True(await service.PostAsync(zev.Id, expense.Id, CancellationToken.None));
        database.ChangeTracker.Clear();
        failures.Next = new DbUpdateException("Unexpected provider failure.", new InvalidOperationException("CHECK"));
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ReverseAsync(zev.Id, expense.Id, CancellationToken.None));
        database.ChangeTracker.Clear();
        failures.Next = new DbUpdateConcurrencyException("Row changed by another writer.");
        Assert.False(await service.ReverseAsync(zev.Id, expense.Id, CancellationToken.None));
        Assert.Single(database.FundTransactions);
    }

    [Fact]
    public void InterestRequiresConfiguredCoverageAndDayConvention()
    {
        var start = new DateOnly(2026, 1, 1);
        var due = new DateOnly(2026, 1, 1);
        var asOf = new DateOnly(2026, 1, 31);
        Assert.Null(InterestCalculation.Calculate(100m, due, asOf, []));
        Assert.Equal(0.82m, InterestCalculation.Calculate(100m, due, asOf,
            [new InterestRatePolicy { ValidFrom = start, AnnualRatePercent = 10m, DaysPerYear = 365 }]));
    }

    [Fact]
    public void FileValidationRejectsSpoofedOrOversizedPayloads()
    {
        Assert.True(LocalFileStorage.ValidContent("%PDF-1.4\n"u8.ToArray(), "application/pdf"));
        Assert.False(LocalFileStorage.ValidContent("not a pdf"u8.ToArray(), "application/pdf"));
        Assert.False(LocalFileStorage.ValidContent(new byte[5_000_001], "image/png"));
        Assert.False(LocalFileStorage.ValidContent("<script/>"u8.ToArray(), "text/html"));
    }
}
