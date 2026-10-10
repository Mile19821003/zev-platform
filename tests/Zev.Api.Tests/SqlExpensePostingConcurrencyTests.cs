using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlExpensePostingConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPostingOfSameExpenseKeepsSingleLedgerEntry()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevExpenseRace_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "ERACE", Name = "Expense race" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "ERACE", Name = "Expense race" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Regular" };
            var supplier = new Supplier { OrganizationId = organization.Id, Name = "Supplier" };
            database.AddRange(organization, zev, fund, supplier);
            var expenses = Enumerable.Range(1, 5).Select(index => new Expense { OrganizationId = organization.Id,
                ZevId = zev.Id, FundId = fund.Id, SupplierId = supplier.Id, InvoiceNumber = $"INV-{index}",
                Category = "Repair", InvoiceDate = new DateOnly(2026, 9, 1), Amount = 10m * index }).ToList();
            database.AddRange(expenses);
            await database.SaveChangesAsync();

            // Several rounds raise the chance of hitting both outcomes: the loser either sees the expense
            // already posted, or is rejected by SQL Server (deadlock victim / duplicate ledger entry).
            foreach (var expense in expenses)
            {
                await using var first = new ZevDbContext(options, new HttpContextAccessor());
                await using var second = new ZevDbContext(options, new HttpContextAccessor());
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<bool> PostAsync(ZevDbContext context)
                {
                    await start.Task;
                    return await new ExpensePostingService(context).PostAsync(zev.Id, expense.Id, CancellationToken.None);
                }
                var posts = new[] { PostAsync(first), PostAsync(second) };
                start.SetResult();
                var results = await Task.WhenAll(posts);
                Assert.Single(results, posted => posted);
                Assert.Equal(1, await database.FundTransactions.AsNoTracking()
                    .CountAsync(item => item.SourceId == expense.Id && item.Kind == FundTransactionKind.Expense));
                Assert.Equal(ExpenseStatus.Posted, await database.Expenses.AsNoTracking()
                    .Where(item => item.Id == expense.Id).Select(item => item.Status).SingleAsync());
            }
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
