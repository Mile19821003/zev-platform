using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260930180000_InvoiceV2")]
public sealed class InvoiceV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Invoices", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            OrganizationId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ZevId = table.Column<Guid>("uniqueidentifier", nullable: false),
            BillingRunId = table.Column<Guid>("uniqueidentifier", nullable: false),
            PropertyUnitId = table.Column<Guid>("uniqueidentifier", nullable: false),
            PersonId = table.Column<Guid>("uniqueidentifier", nullable: false),
            InvoiceNumber = table.Column<string>("nvarchar(80)", maxLength: 80, nullable: false),
            NumberYear = table.Column<int>("int", nullable: false),
            NumberSequence = table.Column<int>("int", nullable: false),
            Period = table.Column<DateOnly>("date", nullable: false),
            IssueDate = table.Column<DateOnly>("date", nullable: false),
            DueDate = table.Column<DateOnly>("date", nullable: false),
            DocumentStatus = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            Currency = table.Column<string>("nvarchar(3)", maxLength: 3, nullable: false),
            PreviousBalanceSnapshot = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            CurrentChargeTotal = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            DiscountTotal = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            TotalAmount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            TotalDueAtIssue = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            PaymentReferenceSnapshot = table.Column<string>("nvarchar(120)", maxLength: 120, nullable: false),
            ZevNameSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            ZevAddressSnapshot = table.Column<string>("nvarchar(250)", maxLength: 250, nullable: true),
            ZevTaxIdentifierSnapshot = table.Column<string>("nvarchar(32)", maxLength: 32, nullable: true),
            ZevEmailSnapshot = table.Column<string>("nvarchar(320)", maxLength: 320, nullable: true),
            ZevPhoneSnapshot = table.Column<string>("nvarchar(40)", maxLength: 40, nullable: true),
            PersonNameSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            PersonAddressSnapshot = table.Column<string>("nvarchar(250)", maxLength: 250, nullable: true),
            UnitCodeSnapshot = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            UnitTypeSnapshot = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: false),
            UnitAreaSnapshot = table.Column<decimal>("decimal(12,3)", precision: 12, scale: 3, nullable: false),
            UnitAddressSnapshot = table.Column<string>("nvarchar(250)", maxLength: 250, nullable: true),
            CreatedUtc = table.Column<DateTimeOffset>("datetimeoffset", nullable: false),
            CreatedByUserId = table.Column<Guid>("uniqueidentifier", nullable: true),
            IssuedUtc = table.Column<DateTimeOffset>("datetimeoffset", nullable: false),
            IssuedByUserId = table.Column<Guid>("uniqueidentifier", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Invoices", item => item.Id);
            table.UniqueConstraint("AK_Invoices_OrganizationId_ZevId_Id", item => new { item.OrganizationId, item.ZevId, item.Id });
            table.CheckConstraint("CK_Invoices_NumberSequence", "[NumberSequence] > 0");
            table.CheckConstraint("CK_Invoices_CurrentTotal", "[CurrentChargeTotal] >= 0 AND [TotalAmount] >= 0 AND [DiscountTotal] >= 0");
            table.ForeignKey("FK_Invoices_BillingRuns_OrganizationId_ZevId_BillingRunId",
                item => new { item.OrganizationId, item.ZevId, item.BillingRunId }, "BillingRuns",
                new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_Invoices_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                item => new { item.OrganizationId, item.ZevId, item.PropertyUnitId }, "PropertyUnits",
                new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_Invoices_People_OrganizationId_PersonId",
                item => new { item.OrganizationId, item.PersonId }, "People",
                new[] { "OrganizationId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_Invoices_ZevId_InvoiceNumber", "Invoices",
            new[] { "ZevId", "InvoiceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_Invoices_ZevId_NumberYear_NumberSequence", "Invoices",
            new[] { "ZevId", "NumberYear", "NumberSequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_Invoices_ZevId_Period_PropertyUnitId_PersonId", "Invoices",
            new[] { "ZevId", "Period", "PropertyUnitId", "PersonId" }, unique: true);
        migrationBuilder.CreateIndex("IX_Invoices_ZevId_BillingRunId", "Invoices", new[] { "ZevId", "BillingRunId" });
        migrationBuilder.CreateIndex("IX_Invoices_OrganizationId_ZevId_BillingRunId", "Invoices",
            new[] { "OrganizationId", "ZevId", "BillingRunId" });
        migrationBuilder.CreateIndex("IX_Invoices_OrganizationId_ZevId_PropertyUnitId", "Invoices",
            new[] { "OrganizationId", "ZevId", "PropertyUnitId" });
        migrationBuilder.CreateIndex("IX_Invoices_OrganizationId_PersonId", "Invoices",
            new[] { "OrganizationId", "PersonId" });

        migrationBuilder.CreateTable("InvoiceLines", table => new
        {
            Id = table.Column<Guid>("uniqueidentifier", nullable: false),
            OrganizationId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ZevId = table.Column<Guid>("uniqueidentifier", nullable: false),
            InvoiceId = table.Column<Guid>("uniqueidentifier", nullable: false),
            ChargeId = table.Column<Guid>("uniqueidentifier", nullable: false),
            TariffVersionId = table.Column<Guid>("uniqueidentifier", nullable: false),
            FundId = table.Column<Guid>("uniqueidentifier", nullable: false),
            CodeSnapshot = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            DescriptionSnapshot = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            UnitOfMeasureSnapshot = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: false),
            Quantity = table.Column<decimal>("decimal(18,3)", precision: 18, scale: 3, nullable: false),
            UnitPrice = table.Column<decimal>("decimal(18,4)", precision: 18, scale: 4, nullable: false),
            OwnershipShareSnapshot = table.Column<decimal>("decimal(9,6)", precision: 9, scale: 6, nullable: false),
            DiscountType = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            DiscountValue = table.Column<decimal>("decimal(18,4)", precision: 18, scale: 4, nullable: false),
            DiscountAmount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            AmountBeforeDiscount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            FinalAmount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
            SortOrder = table.Column<int>("int", nullable: false),
            ShowOnInvoice = table.Column<bool>("bit", nullable: false),
            NoteSnapshot = table.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: true),
            FundNameSnapshot = table.Column<string>("nvarchar(120)", maxLength: 120, nullable: false),
            BankAccountSnapshot = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: true),
            BankNameSnapshot = table.Column<string>("nvarchar(120)", maxLength: 120, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_InvoiceLines", item => item.Id);
            table.CheckConstraint("CK_InvoiceLines_Amounts", "[AmountBeforeDiscount] >= 0 AND [DiscountAmount] >= 0 AND [FinalAmount] >= 0");
            table.CheckConstraint("CK_InvoiceLines_Quantity", "[Quantity] > 0");
            table.ForeignKey("FK_InvoiceLines_Invoices_OrganizationId_ZevId_InvoiceId",
                item => new { item.OrganizationId, item.ZevId, item.InvoiceId }, "Invoices",
                new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_InvoiceLines_Charges_OrganizationId_ZevId_ChargeId",
                item => new { item.OrganizationId, item.ZevId, item.ChargeId }, "Charges",
                new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_InvoiceLines_ChargeId", "InvoiceLines", "ChargeId", unique: true);
        migrationBuilder.CreateIndex("IX_InvoiceLines_InvoiceId_SortOrder", "InvoiceLines",
            new[] { "InvoiceId", "SortOrder" });
        migrationBuilder.CreateIndex("IX_InvoiceLines_OrganizationId_ZevId_InvoiceId", "InvoiceLines",
            new[] { "OrganizationId", "ZevId", "InvoiceId" });
        migrationBuilder.CreateIndex("IX_InvoiceLines_OrganizationId_ZevId_ChargeId", "InvoiceLines",
            new[] { "OrganizationId", "ZevId", "ChargeId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Issued invoices are immutable financial records; restore a reviewed backup instead.");
    }
}
