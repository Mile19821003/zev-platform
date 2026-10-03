using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlReconciliationConcurrencyTests
{
    [Fact]
    public async Task ConcurrentManualAllocationUsesPaymentOnlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevReconcileRace_{Guid.NewGuid():N}"
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
            var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, AccountNumber = "RACE-ACCOUNT", CurrencyCode = "BAM" };
            var tariff = new TariffVersion { OrganizationId = organization.Id, ZevId = zev.Id,
                Fund = fund, Code = "FIX", Name = "Fixed", Rate = 100m, Version = 1,
                Basis = TariffBasis.FixedUnit, ValidFrom = new DateOnly(2026, 1, 1) };
            var charge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnit = unit, Person = person, Fund = fund, TariffVersion = tariff,
                Period = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 9, 20),
                Amount = 100m, Reference = "CHARGE-RACE" };
            database.AddRange(organization, zev, building, entrance, unit, person, fund, account, tariff, charge);
            await database.SaveChangesAsync();
            var parser = new DelimitedStatementParser("CSV", ',');
            var imported = await new PaymentReconciliationEngine(database, [parser]).ImportAsync(
                zev.Id, account.Id, "race.csv", "CSV",
                Encoding.UTF8.GetBytes("date,amount,reference\n2026-09-01,20.00,UNKNOWN"), CancellationToken.None);
            Assert.NotNull(imported.Import);
            var transactionId = await database.BankTransactions.Select(item => item.Id).SingleAsync();

            await using var firstDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var secondDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> AllocateAsync(ZevDbContext context)
            {
                await start.Task;
                return await new PaymentReconciliationEngine(context, [parser]).AllocateManuallyAsync(
                    zev.Id, transactionId, [(charge.Id, 20m)], CancellationToken.None);
            }
            var first = AllocateAsync(firstDatabase);
            var second = AllocateAsync(secondDatabase);
            start.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, success => success);
            Assert.Equal(20m, await database.PaymentAllocations.AsNoTracking().SumAsync(item => item.Amount));

            var manual = await new PaymentWorkflowService(database).CreateManualAsync(zev.Id,
                new ManualPaymentInput(Guid.NewGuid(), person.Id, null, fund.Id,
                    new DateOnly(2026, 9, 1), 80m, "BAM", "Migration", "Race test", null, null, null),
                null, CancellationToken.None);
            var manualPaymentId = Assert.IsType<Guid>(manual.PaymentId);
            await using var thirdDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var fourthDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var manualStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PaymentWorkflowResult> AllocateCreditAsync(ZevDbContext context)
            {
                await manualStart.Task;
                return await new PaymentWorkflowService(context).AllocateAsync(zev.Id, manualPaymentId,
                    Guid.NewGuid(), [new PaymentAllocationInput(charge.Id, 80m)], CancellationToken.None);
            }
            var third = AllocateCreditAsync(thirdDatabase);
            var fourth = AllocateCreditAsync(fourthDatabase);
            manualStart.SetResult();
            var manualResults = await Task.WhenAll(third, fourth);
            Assert.Single(manualResults, result => result.Success);
            Assert.Equal(100m, await database.PaymentAllocations.AsNoTracking().SumAsync(item => item.Amount));

            foreach (var requestedAmount in new[] { 100m, 60m })
            {
                var raceCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                    PropertyUnitId = unit.Id, PersonId = person.Id, FundId = fund.Id,
                    Period = new DateOnly(2026, 12, 1), DueDate = new DateOnly(2026, 12, 20),
                    Amount = 100m, Reference = $"ALLOCATION-{Guid.NewGuid():N}" };
                database.Charges.Add(raceCharge);
                await database.SaveChangesAsync();
                var raceRequestId = Guid.NewGuid();
                var racePayment = await new PaymentWorkflowService(database).CreateManualAsync(zev.Id,
                    new ManualPaymentInput(raceRequestId, person.Id, null, fund.Id,
                        new DateOnly(2026, 9, 1), 100m, "BAM", "Migration", "Allocation race", null, null, null),
                    null, CancellationToken.None);
                Assert.True(racePayment.Success, racePayment.Error);
                var racePaymentId = Assert.IsType<Guid>(racePayment.PaymentId);
                await using var leftDatabase = new ZevDbContext(options, new HttpContextAccessor());
                await using var rightDatabase = new ZevDbContext(options, new HttpContextAccessor());
                var raceStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<PaymentWorkflowResult> AttemptAsync(ZevDbContext context)
                {
                    await raceStart.Task;
                    return await new PaymentWorkflowService(context).AllocateAsync(zev.Id, racePaymentId,
                        Guid.NewGuid(), [new PaymentAllocationInput(raceCharge.Id, requestedAmount)],
                        CancellationToken.None);
                }
                var left = AttemptAsync(leftDatabase);
                var right = AttemptAsync(rightDatabase);
                raceStart.SetResult();
                var raceResults = await Task.WhenAll(left, right);
                Assert.Single(raceResults, result => result.Success);
                Assert.Single(raceResults, result => !result.Success && !string.IsNullOrWhiteSpace(result.Error));
                var allocations = await database.PaymentAllocations.AsNoTracking()
                    .Where(item => item.PaymentId == racePaymentId).ToListAsync();
                var allocation = Assert.Single(allocations);
                Assert.Equal(raceCharge.Id, allocation.ChargeId);
                var allocated = allocations.Sum(item => item.Amount);
                Assert.Equal(requestedAmount, allocated);
                Assert.True(allocated <= 100m);
                Assert.Equal(1, await database.Payments.AsNoTracking()
                    .CountAsync(item => item.RequestId == raceRequestId));
                var paymentAmount = await database.Payments.AsNoTracking()
                    .Where(item => item.Id == racePaymentId).Select(item => item.Amount).SingleAsync();
                Assert.Equal(requestedAmount == 100m ? 0m : 40m, paymentAmount - allocated);
                Assert.True(allocated <= raceCharge.Amount);
            }

            var replacement = new Person { OrganizationId = organization.Id, DisplayName = "Correct payer" };
            var replacementCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnitId = unit.Id, Person = replacement, FundId = fund.Id,
                Period = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 30),
                Amount = 80m, Reference = "CORRECT-PAYER" };
            database.AddRange(replacement, replacementCharge);
            await database.SaveChangesAsync();
            await using var fifthDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var sixthDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var payerStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PaymentWorkflowResult> CorrectAsync(ZevDbContext context)
            {
                await payerStart.Task;
                return await new PaymentWorkflowService(context).ChangePayerAsync(zev.Id,
                    manualPaymentId, replacement.Id, "Bank payer correction", null, null,
                    CancellationToken.None);
            }
            var fifth = CorrectAsync(fifthDatabase);
            var sixth = CorrectAsync(sixthDatabase);
            payerStart.SetResult();
            var payerResults = await Task.WhenAll(fifth, sixth);
            Assert.Single(payerResults, result => result.Success);
            Assert.Single(await database.PaymentIdentificationHistories.AsNoTracking()
                .Where(item => item.PaymentId == manualPaymentId).ToListAsync());
            Assert.Equal(0m, await database.PaymentAllocations.AsNoTracking()
                .Where(item => item.PaymentId == manualPaymentId).SumAsync(item => item.Amount));

            await using var seventhDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var eighthDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var reuseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PaymentWorkflowResult> ReuseAsync(ZevDbContext context)
            {
                await reuseStart.Task;
                return await new PaymentWorkflowService(context).AllocateAsync(zev.Id, manualPaymentId,
                    Guid.NewGuid(), [new PaymentAllocationInput(replacementCharge.Id, 80m)],
                    CancellationToken.None);
            }
            var seventh = ReuseAsync(seventhDatabase);
            var eighth = ReuseAsync(eighthDatabase);
            reuseStart.SetResult();
            Assert.Single(await Task.WhenAll(seventh, eighth), result => result.Success);
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                WITH Numbers AS (
                    SELECT TOP (1001) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS Number
                    FROM sys.all_objects a CROSS JOIN sys.all_objects b
                )
                INSERT INTO Charges
                    (Id, OrganizationId, ZevId, PropertyUnitId, PersonId, FundId,
                     Period, DueDate, BasisQuantity, RateSnapshot, OwnershipShareSnapshot,
                     Amount, Reference, CreatedUtc, IsManualOverride)
                SELECT NEWID(), {organization.Id}, {zev.Id}, {unit.Id}, {replacement.Id}, {fund.Id},
                    '2026-11-01', '2026-11-30', 1, 1, 1, 1,
                    'SQL-BULK-' + CONVERT(varchar(5), Number), SYSUTCDATETIME(), 0
                FROM Numbers
                """);
            var card = await new OwnerCardService(database, new ConfigurationBuilder().Build())
                .ReadAsync(zev.Id, replacement.Id, null, null, 1, 20, CancellationToken.None);
            Assert.Equal(2, card.Balances.Count);
            var bamBalance = Assert.Single(card.Balances, item => item.CurrencyCode == "BAM");
            Assert.Equal(80m, bamBalance.TotalCharges);
            Assert.Equal(80m, bamBalance.TotalPayments);
            Assert.Equal(0m, bamBalance.Open);
            Assert.Equal(0m, bamBalance.Credit);
            Assert.Equal(0m, bamBalance.NetBalance);
            var unknownBalance = Assert.Single(card.Balances, item => item.CurrencyCode == "UNKNOWN");
            Assert.Equal(1001m, unknownBalance.TotalCharges);
            Assert.Equal(0m, unknownBalance.TotalPayments);
            Assert.Equal(1001m, unknownBalance.Open);
            Assert.Equal(0m, unknownBalance.Credit);
            Assert.Equal(1001m, unknownBalance.NetBalance);
            Assert.Equal(1002, card.TotalCount - 2);

            var historicCharge = new Charge { OrganizationId = organization.Id, ZevId = zev.Id,
                PropertyUnitId = unit.Id, PersonId = replacement.Id, FundId = fund.Id,
                Amount = 100m, Period = new DateOnly(2026, 8, 1), DueDate = new DateOnly(2026, 8, 31),
                Reference = "SQL-HIST", CreatedUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) };
            var historicPayment = new Payment { OrganizationId = organization.Id, ZevId = zev.Id,
                FundId = fund.Id, PersonId = replacement.Id, SourceType = PaymentSourceType.Manual,
                RequestId = Guid.NewGuid(), ManualMethod = "Migration", ManualReason = "As-of test",
                Amount = 100m, CurrencyCode = "BAM", PaidOn = new DateOnly(2026, 8, 10),
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero) };
            var historicAllocation = new PaymentAllocation { OrganizationId = organization.Id,
                ZevId = zev.Id, Payment = historicPayment, Charge = historicCharge, Amount = 100m,
                CreatedUtc = new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero) };
            database.AddRange(historicCharge, historicPayment, historicAllocation);
            await database.SaveChangesAsync();
            database.PaymentAllocations.Add(new PaymentAllocation { OrganizationId = organization.Id,
                ZevId = zev.Id, PaymentId = historicPayment.Id, ChargeId = historicCharge.Id,
                Amount = -100m, ReversesAllocationId = historicAllocation.Id,
                ReversalReason = "Correction",
                CreatedUtc = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) });
            await database.SaveChangesAsync();
            var service = new OwnerCardService(database, new ConfigurationBuilder().Build());
            var before = await service.ReadAsync(zev.Id, replacement.Id, null,
                new DateOnly(2026, 8, 31), 1, 20, CancellationToken.None);
            var after = await service.ReadAsync(zev.Id, replacement.Id, null,
                new DateOnly(2026, 9, 6), 1, 20, CancellationToken.None);
            Assert.Equal(0m, Assert.Single(before.Balances).Open);
            Assert.Equal(100m, Assert.Single(after.Balances).Open);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
