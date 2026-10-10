using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Payments;

namespace Zev.Api.Tests;

// The balance queries (bank transaction → import → account, reversal → reversed movement) on SQL Server.
[Collection("Sql integration")]
public sealed class SqlBankAccountBalanceTests
{
    [Fact]
    public async Task BalanceTranslatesAndAddsUpOnSqlServer()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = $"ZevBankBalance_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "SBAL", Name = "Saldo" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "SBAL", Name = "Saldo" };
            var fund = new Fund { OrganizationId = organization.Id, ZevId = zev.Id, Kind = FundKind.Regular, Name = "Redovni" };
            var account = new BankAccount { OrganizationId = organization.Id, ZevId = zev.Id, Fund = fund,
                AccountNumber = "1610000012345678", CurrencyCode = "BAM" };
            var person = new Person { Organization = organization, DisplayName = "Vlasnik" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "Zgrada" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id, Building = building, Code = "E", Name = "Ulaz" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id, BuildingId = building.Id,
                Entrance = entrance, Code = "A-01", AreaSquareMeters = 50m };
            database.AddRange(organization, zev, fund, account, person, building, entrance, unit,
                new Ownership { OrganizationId = organization.Id, ZevId = zev.Id, PropertyUnit = unit, Person = person,
                    ValidFrom = new DateOnly(2026, 1, 1), Share = 1m });
            await database.SaveChangesAsync();

            var engine = new PaymentReconciliationEngine(database, [new Camt053StatementParser()]);
            const string camt = "<Document><BkToCstmrStmt><Stmt><FrToDt><FrDtTm>2026-09-01T00:00:00</FrDtTm><ToDtTm>2026-09-30T23:59:59</ToDtTm></FrToDt>"
                + "<Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"BAM\">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd></Bal>"
                + "<Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy=\"BAM\">120.00</Amt><CdtDbtInd>CRDT</CdtDbtInd></Bal>"
                + "<Ntry><Amt Ccy=\"BAM\">30.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><BookgDt><Dt>2026-09-05</Dt></BookgDt></Ntry>"
                + "<Ntry><Amt Ccy=\"BAM\">10.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><BookgDt><Dt>2026-09-07</Dt></BookgDt></Ntry>"
                + "</Stmt></BkToCstmrStmt></Document>";
            var imported = await engine.ImportAsync(zev.Id, account.Id, "izvod.xml", "CAMT053", Encoding.UTF8.GetBytes(camt),
                CancellationToken.None);
            Assert.True(imported.Import is not null, imported.Error);
            var manual = await new PaymentWorkflowService(database).CreateManualAsync(zev.Id,
                new ManualPaymentInput(Guid.NewGuid(), person.Id, null, fund.Id, new DateOnly(2026, 9, 15), 50m, "BAM",
                    "Transfer", "Bez izvoda", null, null, null), null, CancellationToken.None);
            Assert.True(manual.Success, manual.Error);
            Assert.Equal(ManualPaymentReversalStatus.Reversed, (await new PaymentWorkflowService(database)
                .ReverseManualAsync(zev.Id, manual.PaymentId!.Value, "Greška", null, CancellationToken.None)).Status);

            database.ChangeTracker.Clear();
            var stored = await database.BankStatementImports.AsNoTracking().SingleAsync();
            Assert.Equal((100m, 120m), (stored.OpeningBalance, stored.ClosingBalance));
            Assert.Equal(new DateOnly(2026, 9, 30), stored.StatementTo);
            var balance = await new BankAccountBalanceService(database).ReadAsync(zev.Id, account.Id, CancellationToken.None);
            Assert.NotNull(balance);
            Assert.Equal(120m, balance.Bank.ReportedBalance!.Amount);
            Assert.Equal(120m, balance.Bank.DerivedBalance);
            Assert.True(balance.Bank.DerivedMatchesReported);
            Assert.Equal(30m, balance.Fund.Balance);
            Assert.Equal(30m, balance.Fund.FromThisAccount);
            Assert.Equal(0m, balance.Fund.ManualPayments);
            Assert.Equal(-10m, balance.Reconciliation.BankOutflowsNotInFund);
            Assert.Equal(0m, balance.Reconciliation.Unexplained);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
