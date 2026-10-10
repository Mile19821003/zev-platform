using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Zev.Api.Data;

namespace Zev.Api.Tests;

public sealed class BillingMigrationTests
{
    [Fact]
    public void PaymentIntegrityMigrationOnlyAddsIdentificationHistory()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20261001100000_PaymentsV2", "20261001110000_PaymentIntegrity");
        Assert.Contains("CREATE TABLE [PaymentIdentificationHistories]", script);
        Assert.Contains("CK_PaymentIdentificationHistories_Reason", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }

    [Fact]
    public void RegistryListIndexMigrationIsAdditive()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20261001110000_PaymentIntegrity", "20261003100000_RegistryListIndex");
        Assert.Contains("CREATE INDEX [IX_PropertyUnits_ZevId_EntranceId_Code_Id]", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }

    [Fact]
    public void HodogramRaciMigrationIsAdditive()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20261003100000_RegistryListIndex", "20261010063920_HodogramRaci");
        Assert.Contains("CREATE TABLE [Hodograms]", script);
        Assert.Contains("CREATE TABLE [HodogramAssignments]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_HodogramAssignments_StepId_Role]", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("DELETE FROM", script);
        Assert.DoesNotContain("ALTER TABLE [PropertyUnits]", script);
    }
    [Fact]
    public void CurrentSqlServerModelMatchesMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var snapshot = database.GetService<IModelRuntimeInitializer>()
            .Initialize(database.GetService<IMigrationsAssembly>().ModelSnapshot!.Model, designTime: true);
        var current = database.GetService<IDesignTimeModel>().Model;
        var operations = database.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel());
        Assert.True(operations.Count == 0, string.Join("; ", operations.Select(item =>
            $"{item.GetType().Name}: {item.GetType().GetProperty("Table")?.GetValue(item)} " +
            $"{item.GetType().GetProperty("Name")?.GetValue(item)}")));
    }

    [Fact]
    public void BillingMigrationGeneratesIncrementalSqlWithoutConnectingToDatabase()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260927153424_Phase7CommunityVoting", "20260928120000_BillingItemWorkflow");
        Assert.Contains("ALTER TABLE [TariffVersions] ADD [DisplayOrder]", script);
        Assert.Contains("ALTER TABLE [Payments] ADD [Status]", script);
        Assert.Contains("ALTER TABLE [Payments] ADD [ReversedOn]", script);
    }

    [Fact]
    public void PrivacyMigrationPreservesExistingBusinessTables()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260928120000_BillingItemWorkflow", "20260930120000_EffectiveAccessPrivacy");
        Assert.Contains("CREATE TABLE [AccessOverrides]", script);
        Assert.Contains("ALTER TABLE [People] ADD [UserLinkStatus]", script);
        Assert.Contains("UPDATE [Memberships] SET [Role] = 'OrganizationAdministrator'", script);
        Assert.DoesNotContain("DROP TABLE [Ownerships]", script);
        Assert.DoesNotContain("DROP TABLE [Charges]", script);
    }

    [Fact]
    public void PasswordMigrationPreservesUsersAndAuditHistory()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260930120000_EffectiveAccessPrivacy", "20260930150000_PasswordManagement");
        Assert.Contains("ALTER TABLE [Users] ADD [MustChangePassword]", script);
        Assert.Contains("ALTER TABLE [Users] ADD [SecurityVersion]", script);
        Assert.Contains("ALTER TABLE [RefreshSessions] ADD [SecurityVersion]", script);
        Assert.Contains("ALTER TABLE [AuditLogs] ALTER COLUMN [Action] nvarchar(40)", script);
        Assert.DoesNotContain("DROP TABLE [Users]", script);
        Assert.DoesNotContain("DROP TABLE [RefreshSessions]", script);
    }

    [Fact]
    public void InvoiceMigrationAddsTablesWithoutAlteringExistingCharges()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260930150000_PasswordManagement", "20260930180000_InvoiceV2");
        Assert.Contains("CREATE TABLE [Invoices]", script);
        Assert.Contains("CREATE TABLE [InvoiceLines]", script);
        Assert.Contains("IX_Invoices_ZevId_InvoiceNumber", script);
        Assert.Contains("IX_Invoices_ZevId_NumberYear_NumberSequence", script);
        Assert.Contains("CK_InvoiceLines_Amounts", script);
        Assert.Contains("CK_InvoiceLines_Quantity", script);
        Assert.Contains("FK_InvoiceLines_Charges_OrganizationId_ZevId_ChargeId", script);
        Assert.Contains("FK_InvoiceLines_Invoices_OrganizationId_ZevId_InvoiceId", script);
        Assert.Contains("IX_InvoiceLines_ChargeId", script);
        Assert.DoesNotContain("DROP TABLE [Charges]", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("ALTER TABLE [Charges]", script);
    }

    [Fact]
    public void StabilizationMigrationIsAdditiveAndSupportsFreeManualLines()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260930180000_InvoiceV2", "20260930190000_InvoiceV2Stabilization");
        Assert.Contains("ALTER TABLE [Funds] ADD [RequiresBankAccountForInvoice] bit NOT NULL DEFAULT CAST(0 AS bit)", script);
        Assert.Contains("ALTER TABLE [InvoiceLines] ALTER COLUMN [TariffVersionId] uniqueidentifier NULL", script);
        Assert.Contains("ALTER TABLE [Charges] ALTER COLUMN [TariffVersionId] uniqueidentifier NULL", script);
        Assert.Contains("[TariffVersionId] IS NOT NULL", script);
        Assert.Contains("DROP CONSTRAINT [FK_Charges_TariffVersions_TariffVersionId]", script);
        Assert.Contains("ADD CONSTRAINT [FK_Charges_TariffVersions_TariffVersionId]", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }

    [Fact]
    public void InvoiceLifecycleMigrationAddsReversalsWithoutDeletingFinancialData()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>()
            .UseSqlServer("Server=localhost;Database=ZevSnapshotCheck;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        using var database = new ZevDbContext(options, new HttpContextAccessor());
        var script = database.GetService<IMigrator>().GenerateScript(
            "20260930190000_InvoiceV2Stabilization", "20260930200000_InvoiceLifecycle");
        Assert.Contains("CREATE TABLE [FinancialDocuments]", script);
        Assert.Contains("CREATE TABLE [FinancialDocumentLines]", script);
        Assert.Contains("ALTER TABLE [Charges] ADD [ReversesChargeId]", script);
        Assert.Contains("ALTER TABLE [PaymentAllocations] ADD [ReversesAllocationId]", script);
        Assert.Contains("CK_FinancialDocuments_Status", script);
        Assert.Contains("IX_FinancialDocuments_ZevId_RequestId", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("DROP COLUMN", script);
        Assert.DoesNotContain("DELETE FROM", script);
    }
}
