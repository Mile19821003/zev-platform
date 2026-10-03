using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260930200000_InvoiceLifecycle")]
public sealed class InvoiceLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("FinancialDocuments", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            OrganizationId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ZevId = table.Column<Guid>("uniqueidentifier", nullable: false),
            OriginalInvoiceId = table.Column<Guid>("uniqueidentifier", nullable: false),
            RequestId = table.Column<Guid>("uniqueidentifier", nullable: false),
            Type = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            DocumentStatus = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            Series = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            DocumentNumber = table.Column<string>("nvarchar(80)", maxLength: 80, nullable: false),
            NumberYear = table.Column<int>("int", nullable: false),
            NumberSequence = table.Column<int>("int", nullable: false),
            IssueDate = table.Column<DateOnly>("date", nullable: false),
            Reason = table.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            Note = table.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: true),
            SignedAmount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            Currency = table.Column<string>("nvarchar(3)", maxLength: 3, nullable: false),
            ZevNameSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            PersonNameSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            UnitCodeSnapshot = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            OriginalInvoiceNumberSnapshot = table.Column<string>("nvarchar(80)", maxLength: 80, nullable: false),
            CreatedByUserId = table.Column<Guid>("uniqueidentifier", nullable: true),
            CreatedUtc = table.Column<DateTimeOffset>("datetimeoffset", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_FinancialDocuments", item => item.Id);
            table.UniqueConstraint("AK_FinancialDocuments_OrganizationId_ZevId_Id",
                item => new { item.OrganizationId, item.ZevId, item.Id });
            table.CheckConstraint("CK_FinancialDocuments_Amount",
                "([Type] = 'Debit' AND [SignedAmount] > 0) OR ([Type] = 'Credit' AND [SignedAmount] < 0) OR ([Type] = 'Storno' AND [SignedAmount] <= 0)");
            table.CheckConstraint("CK_FinancialDocuments_Status", "[DocumentStatus] = 'Issued'");
            table.ForeignKey("FK_FinancialDocuments_Invoices_OrganizationId_ZevId_OriginalInvoiceId",
                columns: item => new { item.OrganizationId, item.ZevId, item.OriginalInvoiceId },
                principalTable: "Invoices", principalColumns: new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_FinancialDocuments_OriginalInvoiceId", "FinancialDocuments",
            "OriginalInvoiceId", unique: true, filter: "[Type] = 'Storno'");
        migrationBuilder.CreateIndex("IX_FinancialDocuments_OrganizationId_ZevId_OriginalInvoiceId",
            "FinancialDocuments", new[] { "OrganizationId", "ZevId", "OriginalInvoiceId" });
        migrationBuilder.CreateIndex("IX_FinancialDocuments_ZevId_RequestId", "FinancialDocuments",
            new[] { "ZevId", "RequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_FinancialDocuments_ZevId_DocumentNumber", "FinancialDocuments",
            new[] { "ZevId", "DocumentNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_FinancialDocuments_ZevId_Series_NumberYear_NumberSequence", "FinancialDocuments",
            new[] { "ZevId", "Series", "NumberYear", "NumberSequence" }, unique: true);

        migrationBuilder.AddColumn<Guid>("FinancialDocumentId", "Charges", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("ReversesChargeId", "Charges", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("EffectiveOn", "Charges", "date", nullable: true);
        migrationBuilder.DropCheckConstraint("CK_Charges_Amount", "Charges");
        migrationBuilder.AddCheckConstraint("CK_Charges_Amount", "Charges",
            "[Amount] >= 0 OR ([Amount] < 0 AND [ReversesChargeId] IS NOT NULL AND [FinancialDocumentId] IS NOT NULL)");
        migrationBuilder.CreateIndex("IX_Charges_OrganizationId_ZevId_FinancialDocumentId", "Charges",
            new[] { "OrganizationId", "ZevId", "FinancialDocumentId" });
        migrationBuilder.CreateIndex("IX_Charges_ReversesChargeId", "Charges", "ReversesChargeId");
        migrationBuilder.AddForeignKey("FK_Charges_FinancialDocuments_OrganizationId_ZevId_FinancialDocumentId",
            table: "Charges", columns: new[] { "OrganizationId", "ZevId", "FinancialDocumentId" },
            principalTable: "FinancialDocuments", principalColumns: new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_Charges_Charges_ReversesChargeId", "Charges", "ReversesChargeId",
            "Charges", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable("FinancialDocumentLines", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            OrganizationId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ZevId = table.Column<Guid>("uniqueidentifier", nullable: false),
            DocumentId = table.Column<Guid>("uniqueidentifier", nullable: false),
            OriginalInvoiceLineId = table.Column<Guid>("uniqueidentifier", nullable: true),
            ChargeId = table.Column<Guid>("uniqueidentifier", nullable: false),
            FundId = table.Column<Guid>("uniqueidentifier", nullable: false),
            FundNameSnapshot = table.Column<string>("nvarchar(120)", maxLength: 120, nullable: false),
            DescriptionSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            SignedAmount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_FinancialDocumentLines", item => item.Id);
            table.CheckConstraint("CK_FinancialDocumentLines_Amount", "[SignedAmount] <> 0");
            table.ForeignKey("FK_FinancialDocumentLines_FinancialDocuments_OrganizationId_ZevId_DocumentId",
                columns: item => new { item.OrganizationId, item.ZevId, item.DocumentId }, principalTable: "FinancialDocuments",
                principalColumns: new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_FinancialDocumentLines_Charges_OrganizationId_ZevId_ChargeId",
                columns: item => new { item.OrganizationId, item.ZevId, item.ChargeId }, principalTable: "Charges",
                principalColumns: new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_FinancialDocumentLines_InvoiceLines_OriginalInvoiceLineId",
                item => item.OriginalInvoiceLineId, "InvoiceLines", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_FinancialDocumentLines_ChargeId", "FinancialDocumentLines",
            "ChargeId", unique: true);
        migrationBuilder.CreateIndex("IX_FinancialDocumentLines_DocumentId_FundId", "FinancialDocumentLines",
            new[] { "DocumentId", "FundId" });
        migrationBuilder.CreateIndex("IX_FinancialDocumentLines_OrganizationId_ZevId_DocumentId", "FinancialDocumentLines",
            new[] { "OrganizationId", "ZevId", "DocumentId" });
        migrationBuilder.CreateIndex("IX_FinancialDocumentLines_OrganizationId_ZevId_ChargeId", "FinancialDocumentLines",
            new[] { "OrganizationId", "ZevId", "ChargeId" });
        migrationBuilder.CreateIndex("IX_FinancialDocumentLines_OriginalInvoiceLineId", "FinancialDocumentLines",
            "OriginalInvoiceLineId");

        migrationBuilder.AddColumn<Guid>("ReversesAllocationId", "PaymentAllocations", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("FinancialDocumentId", "PaymentAllocations", "uniqueidentifier", nullable: true);
        migrationBuilder.DropCheckConstraint("CK_PaymentAllocations_Amount", "PaymentAllocations");
        migrationBuilder.AddCheckConstraint("CK_PaymentAllocations_Amount", "PaymentAllocations",
            "([Amount] > 0 AND [ReversesAllocationId] IS NULL AND [FinancialDocumentId] IS NULL) OR ([Amount] < 0 AND [ReversesAllocationId] IS NOT NULL AND [FinancialDocumentId] IS NOT NULL)");
        migrationBuilder.DropIndex("IX_PaymentAllocations_PaymentId_ChargeId", "PaymentAllocations");
        migrationBuilder.CreateIndex("IX_PaymentAllocations_PaymentId_ChargeId", "PaymentAllocations",
            new[] { "PaymentId", "ChargeId" }, unique: true, filter: "[ReversesAllocationId] IS NULL");
        migrationBuilder.CreateIndex("IX_PaymentAllocations_ReversesAllocationId_FinancialDocumentId", "PaymentAllocations",
            new[] { "ReversesAllocationId", "FinancialDocumentId" }, unique: true,
            filter: "[ReversesAllocationId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_PaymentAllocations_OrganizationId_ZevId_FinancialDocumentId", "PaymentAllocations",
            new[] { "OrganizationId", "ZevId", "FinancialDocumentId" });
        migrationBuilder.AddForeignKey("FK_PaymentAllocations_PaymentAllocations_ReversesAllocationId",
            "PaymentAllocations", "ReversesAllocationId", "PaymentAllocations", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_PaymentAllocations_FinancialDocuments_OrganizationId_ZevId_FinancialDocumentId",
            table: "PaymentAllocations", columns: new[] { "OrganizationId", "ZevId", "FinancialDocumentId" },
            principalTable: "FinancialDocuments", principalColumns: new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Financial correction history is append-only; restore a reviewed backup instead.");
}
