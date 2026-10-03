using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20261001100000_PaymentsV2")]
public sealed class PaymentsV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("AutoApplyPaymentCredit", "Zevs", "bit", nullable: false,
            defaultValue: false);
        migrationBuilder.DropForeignKey("FK_Payments_BankTransactions_OrganizationId_ZevId_BankTransactionId", "Payments");
        migrationBuilder.DropIndex("IX_Payments_BankTransactionId", "Payments");
        migrationBuilder.DropIndex("IX_Payments_OrganizationId_ZevId_BankTransactionId", "Payments");
        migrationBuilder.AlterColumn<Guid>("BankTransactionId", "Payments", "uniqueidentifier",
            nullable: true, oldClrType: typeof(Guid), oldType: "uniqueidentifier");
        migrationBuilder.CreateIndex("IX_Payments_BankTransactionId", "Payments", "BankTransactionId",
            unique: true, filter: "[BankTransactionId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Payments_OrganizationId_ZevId_BankTransactionId", "Payments",
            new[] { "OrganizationId", "ZevId", "BankTransactionId" });
        migrationBuilder.AddForeignKey("FK_Payments_BankTransactions_OrganizationId_ZevId_BankTransactionId",
            table: "Payments", columns: new[] { "OrganizationId", "ZevId", "BankTransactionId" },
            principalTable: "BankTransactions", principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddColumn<string>("SourceType", "Payments", "nvarchar(20)", maxLength: 20,
            nullable: false, defaultValue: "BankTransaction");
        migrationBuilder.AddColumn<Guid>("RequestId", "Payments", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("ManualReason", "Payments", "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>("ManualMethod", "Payments", "nvarchar(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("ManualReference", "Payments", "nvarchar(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<string>("ManualDescription", "Payments", "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<Guid>("ManualUnitId", "Payments", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("CreatedByUserId", "Payments", "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_Payments_Source", "Payments",
            "([SourceType] = 'BankTransaction' AND [BankTransactionId] IS NOT NULL AND [ManualReason] IS NULL AND [ManualMethod] IS NULL) OR ([SourceType] = 'Manual' AND [BankTransactionId] IS NULL AND [ManualReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ManualReason]))) > 0 AND [ManualMethod] IS NOT NULL AND LEN(LTRIM(RTRIM([ManualMethod]))) > 0 AND [RequestId] IS NOT NULL)");
        migrationBuilder.CreateIndex("IX_Payments_ZevId_RequestId", "Payments", new[] { "ZevId", "RequestId" },
            unique: true, filter: "[RequestId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Payments_OrganizationId_ZevId_ManualUnitId", "Payments",
            new[] { "OrganizationId", "ZevId", "ManualUnitId" });
        migrationBuilder.AddForeignKey("FK_Payments_PropertyUnits_OrganizationId_ZevId_ManualUnitId",
            table: "Payments", columns: new[] { "OrganizationId", "ZevId", "ManualUnitId" },
            principalTable: "PropertyUnits", principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddColumn<Guid>("OperationId", "PaymentAllocations", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("ReversalReason", "PaymentAllocations", "nvarchar(500)",
            maxLength: 500, nullable: true);
        migrationBuilder.DropCheckConstraint("CK_PaymentAllocations_Amount", "PaymentAllocations");
        migrationBuilder.AddCheckConstraint("CK_PaymentAllocations_Amount", "PaymentAllocations",
            "([Amount] > 0 AND [ReversesAllocationId] IS NULL AND [FinancialDocumentId] IS NULL AND [ReversalReason] IS NULL) OR ([Amount] < 0 AND [ReversesAllocationId] IS NOT NULL AND (([FinancialDocumentId] IS NOT NULL AND [ReversalReason] IS NULL) OR ([FinancialDocumentId] IS NULL AND [ReversalReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReversalReason]))) > 0)))");
        migrationBuilder.DropIndex("IX_PaymentAllocations_PaymentId_ChargeId", "PaymentAllocations");
        migrationBuilder.CreateIndex("IX_PaymentAllocations_PaymentId_OperationId_ChargeId", "PaymentAllocations",
            new[] { "PaymentId", "OperationId", "ChargeId" }, unique: true, filter: "[OperationId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Payments V2 preserves append-only financial history; restore a reviewed backup instead.");
}
