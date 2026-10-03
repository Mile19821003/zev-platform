using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Data.Migrations;

namespace Zev.Api.Tests;

public sealed class PaymentsV2MigrationTests
{
    [Fact]
    public void MigrationAddsManualSourceAndCreditPolicyWithoutDroppingFinancialRows()
    {
        var operations = new PaymentsV2().UpOperations;
        Assert.Contains(operations.OfType<AddColumnOperation>(), item => item.Table == "Payments"
            && item.Name == "SourceType" && item.DefaultValue?.ToString() == "BankTransaction");
        Assert.Contains(operations.OfType<AddColumnOperation>(), item => item.Table == "Zevs"
            && item.Name == "AutoApplyPaymentCredit" && item.DefaultValue is false);
        Assert.Contains(operations.OfType<AddCheckConstraintOperation>(), item => item.Table == "Payments"
            && item.Name == "CK_Payments_Source" && item.Sql.Contains("[BankTransactionId] IS NULL"));
        Assert.Contains(operations.OfType<AddCheckConstraintOperation>(), item => item.Table == "PaymentAllocations"
            && item.Name == "CK_PaymentAllocations_Amount" && item.Sql.Contains("[ReversalReason]"));
        Assert.DoesNotContain(operations, item => item is DropTableOperation or DeleteDataOperation);
    }

    [Fact]
    public void PaymentsV2GeneratesIncrementalSqlWithoutRemovingHistory()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260930200000_InvoiceLifecycle", "20261001100000_PaymentsV2");
        Assert.Contains("ALTER TABLE [Payments] ADD [SourceType]", script);
        Assert.Contains("ALTER TABLE [Payments] ALTER COLUMN [BankTransactionId] uniqueidentifier NULL", script);
        Assert.Contains("IX_Payments_ZevId_RequestId", script);
        Assert.Contains("CK_PaymentAllocations_Amount", script);
        Assert.Contains("ALTER TABLE [Zevs] ADD [AutoApplyPaymentCredit]", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }
}
