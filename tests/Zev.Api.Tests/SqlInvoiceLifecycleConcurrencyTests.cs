using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlInvoiceLifecycleConcurrencyTests
{
    [Fact]
    public async Task ConcurrentStornoPostsExactlyOneDocument()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevLifecycleRace_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "RACE", Name = "Race" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "RACE", Name = "Race" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 40m };
            var person = new Person { Organization = organization, DisplayName = "Owner" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Name = "Regular" };
            var run = new BillingRun { OrganizationId = organization.Id, ZevId = zev.Id,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 20),
                TotalAmount = 100m, ChargeCount = 1 };
            var charge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, BillingRun = run,
                Period = run.Period, DueDate = run.DueDate, Amount = 100m, Reference = "LIFECYCLE-RACE" };
            var invoice = new Invoice { OrganizationId = organization.Id, ZevId = zev.Id,
                BillingRunId = run.Id, PropertyUnitId = unit.Id, PersonId = person.Id,
                InvoiceNumber = "INV-2026-000001", NumberYear = 2026, NumberSequence = 1,
                Period = run.Period, IssueDate = run.Period, DueDate = run.DueDate,
                TotalAmount = 100m, CurrentChargeTotal = 100m,
                ZevNameSnapshot = zev.Name, PersonNameSnapshot = person.DisplayName,
                UnitCodeSnapshot = unit.Code, UnitTypeSnapshot = "Apartment" };
            var line = new InvoiceLine { OrganizationId = organization.Id, ZevId = zev.Id,
                Invoice = invoice, Charge = charge, FundId = fund.Id,
                FundNameSnapshot = fund.Name, CodeSnapshot = "FIX", DescriptionSnapshot = "Regular",
                UnitOfMeasureSnapshot = "kom", Quantity = 1m, UnitPrice = 100m,
                AmountBeforeDiscount = 100m, FinalAmount = 100m };
            database.AddRange(organization, zev, building, entrance, unit, person, fund, run, charge, invoice, line);
            await database.SaveChangesAsync();
            await using var firstDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var secondDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<LifecycleResult> PostAsync(ZevDbContext context)
            {
                await start.Task;
                return await new InvoiceLifecycleService(context, new InvoiceNumberProvider())
                    .StornoAsync(zev.Id, invoice.Id,
                        new StornoRequest(Guid.NewGuid(), "Pogreška", null), null, CancellationToken.None);
            }
            var first = PostAsync(firstDatabase);
            var second = PostAsync(secondDatabase);
            start.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, result => result.Document is not null);
            Assert.Single(await database.FinancialDocuments.AsNoTracking().ToListAsync());
            Assert.Equal(0m, await database.Charges.AsNoTracking().SumAsync(item => item.Amount));
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
